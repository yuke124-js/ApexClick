# ApexClick.FeaturePack

FeaturePack now contains production-oriented adapters for graph execution, UI graph editing, AI prompt-to-script, online/local licensing, virtual-screen integration, debugger/plugin/export helpers.

AI output is always marked as requiring user review and playback is blocked until explicit approval.

Standalone export uses the solution-level ApexClick.Runner project and publishes into a temporary build directory before copying only the final EXE + .mscr into the destination.
