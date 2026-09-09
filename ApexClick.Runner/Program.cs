using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System;
using ApexClick.FeaturePack.GraphEngine;
using ApexClick.Models;
using ApexClick.Services.Capture;
using ApexClick.Services.Playback;
using ApexClick.Services.Vision;

var mscrPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory,"scenario.mscr");
if(!File.Exists(mscrPath)) throw new FileNotFoundException("Scenario .mscr not found.",mscrPath);
var script=await MscrScriptSerializer.LoadAsync(mscrPath);
using var hook=new InputHookService();
using var vision=new ImageAnalysisService();
var capture=new ScreenCaptureService();
var timing=new PlaybackTimingService();
var playback=new PlaybackEngine(timing,hook);
if(ExecutionGraphPersistence.TryLoad(script,out var graph) && graph is not null)
{
    var interpreter=new ExecutionGraphInterpreter(playback,new VisionGraphConditionEvaluator(capture,vision),new MscrSubScenarioLoader());
    await interpreter.ExecuteScriptGraphAsync(script, graph, cancellationToken: CancellationToken.None);
}
else await playback.PlayAsync(script,CancellationToken.None);
