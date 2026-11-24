using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;

namespace DroneTrainer.CV
{
    /// <summary>
    /// Represents a detected hardware component on the flight controller board.
    /// </summary>
    public readonly struct ComponentDetectionResult
    {
        public readonly int ClassId;
        public readonly string Label;
        public readonly float Confidence;
        public readonly Rect NormalizedBoundingBox;

        public ComponentDetectionResult(int classId, string label, float confidence, Rect normalizedBoundingBox)
        {
            ClassId = classId;
            Label = label;
            Confidence = confidence;
            NormalizedBoundingBox = normalizedBoundingBox;
        }
    }

    /// <summary>
    /// Manages real-time on-device inference using Unity Sentis and an optimized YOLOv8 ONNX model.
    /// Designed for offline operation on mobile devices with strict frame budget management.
    /// </summary>
    public sealed class SentisYoloDetector : MonoBehaviour, IDisposable
    {
        [Header("Model Configuration")]
        [SerializeField] private ModelAsset _modelAsset;
        [SerializeField] private BackendType _backendType = BackendType.GPUCompute;
        [SerializeField] private string[] _classLabels = new string[]
        {
            "MCU_STM32F405", "IMU_MPU6000", "OSD_AT7456E", "PAD_POWER_VBAT", "PAD_GND", "PAD_ESC_TELEMETRY"
        };

        [Header("Inference Tuning")]
        [SerializeField, Range(0.1f, 0.95f)] private float _confidenceThreshold = 0.45f;
        [SerializeField, Range(0.1f, 0.9f)] private float _iouThreshold = 0.5f;
        [SerializeField, Range(1, 10)] private int _inferenceFrameInterval = 2; // Throttle inference to preserve 60 FPS

        public event Action<IReadOnlyList<ComponentDetectionResult>> OnDetectionsUpdated;

        private Model _runtimeModel;
        private IWorker _engineWorker;
        private TextureTransform _inputTransform;
        private readonly List<ComponentDetectionResult> _resultsBuffer = new List<ComponentDetectionResult>(16);
        private float[] _bestScores;
        private int[] _bestProposalIndices;

        private int _frameCounter;
        private bool _isInitialized;

        private const int ModelInputWidth = 640;
        private const int ModelInputHeight = 640;

        private void Awake()
        {
            InitializeSentisEngine();
        }

        /// <summary>
        /// Initializes the Sentis runtime engine and prepares input texture transformation parameters.
        /// </summary>
        private void InitializeSentisEngine()
        {
            if (_modelAsset == null)
            {
                Debug.LogError("[SentisYoloDetector] ModelAsset is missing! Assign an ONNX model.");
                return;
            }

            try
            {
                _runtimeModel = ModelLoader.Load(_modelAsset);
                _engineWorker = WorkerFactory.CreateWorker(_backendType, _runtimeModel);

                // Configure tensor transformation (Resize to 640x640, channel order RGB, normalized [0, 1])
                _inputTransform = new TextureTransform()
                    .SetDimensions(ModelInputWidth, ModelInputHeight, 3)
                    .SetTensorLayout(TensorLayout.NCHW);

                _bestScores = new float[_classLabels.Length];
                _bestProposalIndices = new int[_classLabels.Length];

                _isInitialized = true;
                Debug.Log($"[SentisYoloDetector] Engine initialized successfully on {_backendType}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SentisYoloDetector] Failed to initialize worker: {ex.Message}");
            }
        }

        /// <summary>
        /// Feeds a camera feed frame into the neural network for object detection.
        /// Throttled to execute once every N frames to eliminate GC spikes and thermal throttling.
        /// </summary>
        /// <param name="frameTexture">Input texture from AR camera background.</param>
        public void ProcessCameraFrame(Texture frameTexture)
        {
            if (!_isInitialized || frameTexture == null) return;

            _frameCounter++;
            if (_frameCounter % _inferenceFrameInterval != 0) return;

            StartCoroutine(ExecuteInferenceRoutine(frameTexture));
        }

        private IEnumerator ExecuteInferenceRoutine(Texture sourceTexture)
        {
            TensorFloat inputTensor = null;

            try
            {
                // Convert camera texture into input tensor on GPU
                inputTensor = TextureConverter.ToTensor(sourceTexture, _inputTransform);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SentisYoloDetector] Texture conversion failed: {ex.Message}");
                yield break;
            }

            // Schedule inference asynchronously
            _engineWorker.Execute(inputTensor);
            inputTensor.Dispose();

            // Fetch output tensor (shape: [1, 4 + classes, 8400] for YOLOv8)
            var outputTensor = _engineWorker.PeekOutput() as TensorFloat;
            if (outputTensor == null) yield break;

            // Wait for GPU sync without blocking main thread
            yield return null;

            ParseYoloOutput(outputTensor);
        }

        /// <summary>
        /// Parses the raw tensor output using a Per-Class Peak Confidence / Lightweight NMS filter
        /// to retain only the highest-scoring candidate per component class exceeding the confidence threshold.
        /// </summary>
        private void ParseYoloOutput(TensorFloat outputTensor)
        {
            _resultsBuffer.Clear();

            int proposalCount = outputTensor.shape[2];
            int classCount = _classLabels.Length;

            if (_bestScores == null || _bestScores.Length != classCount)
            {
                _bestScores = new float[classCount];
                _bestProposalIndices = new int[classCount];
            }

            // Reset per-class peak tracking buffers
            for (int c = 0; c < classCount; c++)
            {
                _bestScores[c] = _confidenceThreshold;
                _bestProposalIndices[c] = -1;
            }

            // Find peak proposal per class across proposals
            for (int i = 0; i < proposalCount; i += 4) // Stride sampling for mobile CPU/GPU optimization
            {
                for (int c = 0; c < classCount; c++)
                {
                    float score = outputTensor[0, 4 + c, i];
                    if (score > _bestScores[c])
                    {
                        _bestScores[c] = score;
                        _bestProposalIndices[c] = i;
                    }
                }
            }

            // Extract bounding boxes for winning class candidates
            for (int c = 0; c < classCount; c++)
            {
                int bestIdx = _bestProposalIndices[c];
                if (bestIdx >= 0)
                {
                    float cx = outputTensor[0, 0, bestIdx] / ModelInputWidth;
                    float cy = outputTensor[0, 1, bestIdx] / ModelInputHeight;
                    float w = outputTensor[0, 2, bestIdx] / ModelInputWidth;
                    float h = outputTensor[0, 3, bestIdx] / ModelInputHeight;

                    Rect bbox = new Rect(cx - (w * 0.5f), cy - (h * 0.5f), w, h);
                    string label = c < _classLabels.Length ? _classLabels[c] : "Unknown";

                    _resultsBuffer.Add(new ComponentDetectionResult(c, label, _bestScores[c], bbox));
                }
            }

            OnDetectionsUpdated?.Invoke(_resultsBuffer);
        }

        public void Dispose()
        {
            _engineWorker?.Dispose();
            _engineWorker = null;
            _isInitialized = false;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
