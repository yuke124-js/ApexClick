namespace ApexClick.Models;

public enum MacroEventType
{
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    KeyDown,
    KeyUp,
    Wait,
    TextInput,

    ConditionScreenTemplate,
    ConditionScreenPixel,
    ConditionOcrText,
    Loop,
    Variable,

    SubScenarioCall
}
