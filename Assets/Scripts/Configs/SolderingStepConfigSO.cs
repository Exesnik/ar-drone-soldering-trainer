using UnityEngine;

namespace DroneTrainer.Configs
{
    public enum SolderingOperationType
    {
        InspectComponentPlacement,
        ApplyFlux,
        SolderPins,
        CheckBridging,
        ContinuityTest
    }

    /// <summary>
    /// ScriptableObject defining validation rules, target component criteria, and instructional copy
    /// for a distinct step in the drone flight controller assembly procedure.
    /// </summary>
    [CreateAssetMenu(fileName = "SolderingStepConfig", menuName = "DroneTrainer/SolderingStepConfig")]
    public sealed class SolderingStepConfigSO : ScriptableObject
    {
        [Header("Step Identity")]
        [SerializeField] private int _stepIndex;
        [SerializeField] private string _stepTitle;
        [SerializeField, TextArea(2, 4)] private string _instructionDescription;

        [Header("Target Component Detection Criteria")]
        [SerializeField] private string _targetClassLabel = "MCU_STM32F405";
        [SerializeField, Range(0.3f, 0.95f)] private float _requiredDetectionConfidence = 0.65f;
        [SerializeField] private SolderingOperationType _operationType = SolderingOperationType.SolderPins;

        [Header("Tolerance & Timing")]
        [SerializeField, Range(0.5f, 5.0f)] private float _dwellTimeSeconds = 1.5f; // Duration component must remain stable in view
        [SerializeField] private Color _highlightColor = new Color(0.12f, 0.75f, 0.95f, 1f);

        public int StepIndex => _stepIndex;
        public string StepTitle => _stepTitle;
        public string InstructionDescription => _instructionDescription;
        public string TargetClassLabel => _targetClassLabel;
        public float RequiredDetectionConfidence => _requiredDetectionConfidence;
        public SolderingOperationType OperationType => _operationType;
        public float DwellTimeSeconds => _dwellTimeSeconds;
        public Color HighlightColor => _highlightColor;
    }
}
