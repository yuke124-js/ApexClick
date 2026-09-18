using System.Windows;
using ApexClick.Models;
using ApexClick.ViewModels;

namespace ApexClick.Views;

public partial class NodePropertiesWindow : Window
{
    private readonly EditorViewModel _vm;
    private readonly string _nodeId;
    private readonly MacroGraphNode _node;

    public NodePropertiesWindow(EditorViewModel vm, string nodeId)
    {
        InitializeComponent();
        _vm = vm;
        _nodeId = nodeId;
        _node = vm.Graph.Nodes.First(n => n.Id == nodeId);
        TypeText.Text = _node.DisplayType;
        Value1.Text = Get("expression", Get("name", Get("path", Get("ocrText", ""))));
        Value2.Text = Get("assetId", Get("templatePath", Get("threshold", Get("value", ""))));
        PropX.Text = Get("pixelX", Get("regionX", "0.5"));
        PropY.Text = Get("pixelY", Get("regionY", "0.5"));
        PropTolerance.Text = Get("pixelTolerance", "10");
        PropW.Text = Get("regionWidth", "1");
        PropH.Text = Get("regionHeight", "1");
        PropRgb.Text = $"{Get("pixelR", "0")},{Get("pixelG", "255")},{Get("pixelB", "0")}";
    }

    private string Get(string key, string fallback = "") =>
        _node.Properties.TryGetValue(key, out var value) ? value : fallback;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        switch (_node.RuntimeType)
        {
            case MacroEventType.Variable:
                _vm.SetNodeProperty(_nodeId, "name", Value1.Text);
                _vm.SetNodeProperty(_nodeId, "value", Value2.Text);
                break;
            case MacroEventType.SubScenarioCall:
                _vm.SetNodeProperty(_nodeId, "path", Value1.Text);
                break;
            case MacroEventType.ConditionScreenTemplate:
                _vm.SetNodeProperty(_nodeId, "expression", string.IsNullOrWhiteSpace(Value1.Text) ? "true" : Value1.Text);
                if (Value2.Text.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || Value2.Text.StartsWith("template-", StringComparison.OrdinalIgnoreCase))
                    _vm.SetNodeProperty(_nodeId, "assetId", Value2.Text);
                else
                    _vm.SetNodeProperty(_nodeId, "threshold", string.IsNullOrWhiteSpace(Value2.Text) ? "0.90" : Value2.Text);
                break;
            case MacroEventType.ConditionScreenPixel:
                _vm.SetNodeProperty(_nodeId, "expression", string.IsNullOrWhiteSpace(Value1.Text) ? "true" : Value1.Text);
                _vm.SetNodeProperty(_nodeId, "pixelX", PropX.Text);
                _vm.SetNodeProperty(_nodeId, "pixelY", PropY.Text);
                _vm.SetNodeProperty(_nodeId, "pixelTolerance", PropTolerance.Text);
                var rgb = PropRgb.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (rgb.Length == 3) { _vm.SetNodeProperty(_nodeId, "pixelR", rgb[0]); _vm.SetNodeProperty(_nodeId, "pixelG", rgb[1]); _vm.SetNodeProperty(_nodeId, "pixelB", rgb[2]); }
                break;
            case MacroEventType.ConditionOcrText:
                _vm.SetNodeProperty(_nodeId, "ocrText", Value1.Text);
                _vm.SetNodeProperty(_nodeId, "regionX", PropX.Text);
                _vm.SetNodeProperty(_nodeId, "regionY", PropY.Text);
                _vm.SetNodeProperty(_nodeId, "regionWidth", PropW.Text);
                _vm.SetNodeProperty(_nodeId, "regionHeight", PropH.Text);
                break;
            case MacroEventType.Loop:
                _vm.SetNodeProperty(_nodeId, "expression", Value1.Text);
                break;
            default:
                if (_node.SourceEvents.Count > 0 && _node.SourceEvents[0].Type == MacroEventType.TextInput)
                {
                    _vm.ReplaceNodePayload(_nodeId, Value1.Text);
                }
                break;
        }
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
