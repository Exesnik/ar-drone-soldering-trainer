using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DroneTrainer.Configs;
using DroneTrainer.CV;

namespace DroneTrainer.FSM
{
    public enum ProcedureState
    {
        Idle,
        DetectingTarget,
        VerifyingComponentPlacement,
        SolderingInProgress,
        StepCompleted,
        ProcedureFinished
    }

    /// <summary>
    /// Finite State Machine governing the step-by-step soldering and assembly workflow.
    /// Relies on Coroutines and Action delegates to ensure thread-safe execution without race conditions.
    /// </summary>
    public sealed class SolderingProcedureFSM : MonoBehaviour
    {
        [Header("Procedure Setup")]
        [SerializeField] private List<SolderingStepConfigSO> _steps = new List<SolderingStepConfigSO>();
        [SerializeField] private SentisYoloDetector _yoloDetector;

        public event Action<SolderingStepConfigSO> OnStepStarted;
        public event Action<SolderingStepConfigSO> OnStepCompleted;
        public event Action<ProcedureState> OnStateChanged;
        public event Action OnEntireProcedureFinished;

        private int _currentStepIndex = 0;
        private ProcedureState _currentState = ProcedureState.Idle;
        private Coroutine _dwellVerificationCoroutine;
        private float _currentDwellTimer;

        public ProcedureState CurrentState => _currentState;
        public SolderingStepConfigSO CurrentStep => (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count) ? _steps[_currentStepIndex] : null;

        private void OnEnable()
        {
            if (_yoloDetector != null)
            {
                _yoloDetector.OnDetectionsUpdated += HandleDetectionsUpdated;
            }
        }

        private void OnDisable()
        {
            if (_yoloDetector != null)
            {
                _yoloDetector.OnDetectionsUpdated -= HandleDetectionsUpdated;
            }
        }

        private void Start()
        {
            StartProcedure();
        }

        public void StartProcedure()
        {
            if (_steps == null || _steps.Count == 0)
            {
                Debug.LogWarning("[SolderingProcedureFSM] No steps configured!");
                return;
            }

            _currentStepIndex = 0;
            ActivateStep(_currentStepIndex);
        }

        private void ActivateStep(int index)
        {
            if (index >= _steps.Count)
            {
                TransitionToState(ProcedureState.ProcedureFinished);
                OnEntireProcedureFinished?.Invoke();
                return;
            }

            _currentStepIndex = index;
            TransitionToState(ProcedureState.DetectingTarget);
            OnStepStarted?.Invoke(CurrentStep);
        }

        private void HandleDetectionsUpdated(IReadOnlyList<ComponentDetectionResult> detections)
        {
            if (_currentState != ProcedureState.DetectingTarget &&
                _currentState != ProcedureState.VerifyingComponentPlacement &&
                _currentState != ProcedureState.SolderingInProgress)
                return;

            SolderingStepConfigSO step = CurrentStep;
            if (step == null) return;

            bool targetFound = false;

            for (int i = 0; i < detections.Count; i++)
            {
                var detection = detections[i];
                if (detection.Label == step.TargetClassLabel && detection.Confidence >= step.RequiredDetectionConfidence)
                {
                    targetFound = true;
                    break;
                }
            }

            if (targetFound)
            {
                if (_currentState == ProcedureState.DetectingTarget)
                {
                    TransitionToState(ProcedureState.VerifyingComponentPlacement);
                    _dwellVerificationCoroutine = StartCoroutine(DwellVerificationRoutine(step));
                }
            }
            else
            {
                if (_currentState == ProcedureState.VerifyingComponentPlacement || _currentState == ProcedureState.SolderingInProgress)
                {
                    // Lost target tracking during verification: reset dwell
                    if (_dwellVerificationCoroutine != null)
                    {
                        StopCoroutine(_dwellVerificationCoroutine);
                        _dwellVerificationCoroutine = null;
                    }
                    TransitionToState(ProcedureState.DetectingTarget);
                }
            }
        }

        private IEnumerator DwellVerificationRoutine(SolderingStepConfigSO step)
        {
            _currentDwellTimer = 0f;
            float midwayThreshold = step.DwellTimeSeconds * 0.5f;
            bool switchedToInProgress = false;

            while (_currentDwellTimer < step.DwellTimeSeconds)
            {
                _currentDwellTimer += Time.deltaTime;

                if (!switchedToInProgress && _currentDwellTimer >= midwayThreshold)
                {
                    switchedToInProgress = true;
                    TransitionToState(ProcedureState.SolderingInProgress);
                }

                yield return null;
            }

            // Step successfully verified
            TransitionToState(ProcedureState.StepCompleted);
            OnStepCompleted?.Invoke(step);

            yield return new WaitForSeconds(1.0f);

            // Advance to next step
            ActivateStep(_currentStepIndex + 1);
        }

        private void TransitionToState(ProcedureState newState)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            OnStateChanged?.Invoke(_currentState);
        }
    }
}
