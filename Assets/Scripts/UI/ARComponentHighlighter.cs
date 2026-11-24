using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using DroneTrainer.Configs;
using DroneTrainer.CV;
using DroneTrainer.FSM;

namespace DroneTrainer.UI
{
    /// <summary>
    /// Renders screen-space/world-space highlighting boxes and procedural guidance cues
    /// over recognized flight controller microchips. Uses DOTween for smooth responsive tweens.
    /// </summary>
    public sealed class ARComponentHighlighter : MonoBehaviour
    {
        [Header("UI Element References")]
        [SerializeField] private RectTransform _highlightBoxRect;
        [SerializeField] private Image _highlightBorderImage;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _componentLabelText;

        [Header("Dependencies")]
        [SerializeField] private SentisYoloDetector _yoloDetector;
        [SerializeField] private SolderingProcedureFSM _procedureFSM;

        private Tween _pulseTween;
        private RectTransform _parentCanvasRect;

        private void Awake()
        {
            if (_highlightBoxRect != null && _highlightBoxRect.parent != null)
            {
                _parentCanvasRect = _highlightBoxRect.parent.GetComponent<RectTransform>();
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        private void OnEnable()
        {
            if (_yoloDetector != null) _yoloDetector.OnDetectionsUpdated += HandleDetections;
            if (_procedureFSM != null) _procedureFSM.OnStepStarted += HandleStepStarted;
        }

        private void OnDisable()
        {
            if (_yoloDetector != null) _yoloDetector.OnDetectionsUpdated -= HandleDetections;
            if (_procedureFSM != null) _procedureFSM.OnStepStarted -= HandleStepStarted;
            _pulseTween?.Kill();
        }

        private void HandleStepStarted(SolderingStepConfigSO step)
        {
            if (_componentLabelText != null && step != null)
            {
                _componentLabelText.text = $"{step.StepTitle}\n<size=12>{step.InstructionDescription}</size>";
            }

            if (_highlightBorderImage != null && step != null)
            {
                _highlightBorderImage.color = step.HighlightColor;
            }
        }

        private void HandleDetections(IReadOnlyList<ComponentDetectionResult> detections)
        {
            SolderingStepConfigSO currentStep = _procedureFSM != null ? _procedureFSM.CurrentStep : null;
            if (currentStep == null)
            {
                FadeOutHighlight();
                return;
            }

            bool matchFound = false;

            for (int i = 0; i < detections.Count; i++)
            {
                var det = detections[i];
                if (det.Label == currentStep.TargetClassLabel)
                {
                    matchFound = true;
                    UpdateHighlightPosition(det.NormalizedBoundingBox);
                    FadeInHighlight();
                    break;
                }
            }

            if (!matchFound)
            {
                FadeOutHighlight();
            }
        }

        private void UpdateHighlightPosition(Rect normalizedRect)
        {
            if (_highlightBoxRect == null || _parentCanvasRect == null) return;

            Vector2 canvasSize = _parentCanvasRect.sizeDelta;

            // Convert normalized [0, 1] YOLO bounding box coordinates to Canvas space
            float x = (normalizedRect.x + normalizedRect.width * 0.5f) * canvasSize.x;
            float y = (1f - (normalizedRect.y + normalizedRect.height * 0.5f)) * canvasSize.y; // Flip Y for UI coordinates
            float w = normalizedRect.width * canvasSize.x;
            float h = normalizedRect.height * canvasSize.y;

            _highlightBoxRect.anchoredPosition = new Vector2(x - canvasSize.x * 0.5f, y - canvasSize.y * 0.5f);
            _highlightBoxRect.sizeDelta = new Vector2(w, h);
        }

        private void FadeInHighlight()
        {
            if (_canvasGroup == null || _canvasGroup.alpha > 0.9f) return;

            _canvasGroup.DOKill();
            _canvasGroup.DOFade(1f, 0.25f);

            if (_pulseTween == null || !_pulseTween.IsActive())
            {
                _pulseTween = _highlightBoxRect.DOScale(1.05f, 0.6f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine);
            }
        }

        private void FadeOutHighlight()
        {
            if (_canvasGroup == null || _canvasGroup.alpha < 0.1f) return;

            _canvasGroup.DOKill();
            _canvasGroup.DOFade(0f, 0.2f);
            _pulseTween?.Kill();
            _pulseTween = null;
        }
    }
}
