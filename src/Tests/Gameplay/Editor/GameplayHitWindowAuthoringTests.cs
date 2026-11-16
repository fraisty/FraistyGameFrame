using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using NUnit.Framework;
using UnityEngine;

namespace FRFrame.Tests.Gameplay.Editor
{
    public sealed class GameplayHitWindowAuthoringTests
    {
        [Test]
        public void WindowCallbacksKeepCapturedScalarsAndOnlyContactGuaranteesAnActor()
        {
            const string defaultKey = "Data.Test.Window.Default";
            const string capturedKey = "Data.Test.Window.Captured";
            const string missingKey = "Data.Test.Window.Missing";
            var document = ScriptableObject.CreateInstance<GameplaySkillDocumentAsset>();
            try
            {
                document.Defaults = new[] { new GameplayScalarInputAuthoring { Key = new GameplayTagReference(defaultKey), Value = 2 } };
                var graph = new GameplayAuthoringGraph();
                var rule = new GameplayAuthoringNode { Payload = new GameplayRuleNodePayload() };
                graph.PrimaryRuleNodeId = rule.Id;
                graph.Nodes.Add(rule);
                var capture = new GameplayAuthoringNode
                {
                    Payload = new GameplayActionNodePayload
                    { Action = new GameplaySkillSetContextValueActionAuthoring { Key = new GameplayTagReference(capturedKey), Value = 3 } }
                };
                graph.Nodes.Add(capture);
                var hitWindow = new GameplayLocalDefinition { Payload = new GameplayHitWindowData() };
                graph.LocalDefinitions.Add(hitWindow);
                var open = new GameplayActionNodePayload { Action = new GameplayOpenHitWindowActionAuthoring() };
                open.Definitions.Add(new GameplayDefinitionBinding { PropertyPath = "Window", LocalId = hitWindow.Id });
                var node = new GameplayAuthoringNode { Payload = open };
                graph.Nodes.Add(node);
                graph.Connections.Add(new GameplayAuthoringConnection { FromNodeId = rule.Id, OutputPortKey = "Actions", ToNodeId = capture.Id });
                graph.Connections.Add(new GameplayAuthoringConnection { FromNodeId = capture.Id, OutputPortKey = "Then", ToNodeId = node.Id });

                foreach (var (port, key) in new[]
                {
                    (nameof(GameplayHitWindowData.OnHit), capturedKey),
                    (nameof(GameplayHitWindowData.OnWindowFinished), defaultKey),
                    (nameof(GameplayHitWindowData.OnCancelled), capturedKey),
                    (nameof(GameplayHitWindowData.OnCancelled), missingKey)
                })
                {
                    var reader = new GameplayAuthoringNode
                    {
                        Payload = new GameplayActionNodePayload
                        {
                            Action = new GameplaySkillBranchActionAuthoring
                            { Condition = new GameplaySkillContextValueConditionAuthoring { Key = new GameplayTagReference(key) } }
                        }
                    };
                    graph.Nodes.Add(reader);
                    graph.Connections.Add(new GameplayAuthoringConnection
                    { FromNodeId = hitWindow.Id, OutputPortKey = port, ToNodeId = reader.Id });
                }
                document.SetAuthoringGraph(graph);

                var analysisType = typeof(GameplaySkillWorkbench).Assembly.GetType(
                    "FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillContextVariables", throwOnError: true);
                var analysis = analysisType.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { document });
                var warnings = (IEnumerable<GameplayAuthoringValidationIssue>)analysisType
                    .GetMethod("Diagnostics", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(analysis, null);
                Assert.That(warnings.Select(issue => issue.Message).Single(), Does.StartWith(missingKey));

                var input = new GameplayGraphContextPolicy.Inputs(true, true, "Original cast target");
                var contact = GameplayGraphContextPolicy.Output(hitWindow.Payload, "OnHit", input);
                Assert.That(contact.Actor && contact.TriggerActor, Is.True);
                foreach (var port in new[] { "OnWindowFinished", "OnCancelled" })
                {
                    var output = GameplayGraphContextPolicy.Output(hitWindow.Payload, port, input);
                    Assert.That(output.Actor, Is.False);
                    Assert.That(output.TriggerActor, Is.False);
                    Assert.That(output.Point && output.TriggerPoint, Is.True);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(document);
            }
        }
    }
}
