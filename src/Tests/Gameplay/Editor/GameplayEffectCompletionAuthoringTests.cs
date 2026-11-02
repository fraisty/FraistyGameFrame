using System;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace FRFrame.Tests.Gameplay.Editor
{
 public sealed class GameplayEffectCompletionAuthoringTests
 {
  static GameplayEffectDefinitionAsset Asset(string id="Test.Completion.Root")
  {var a=ScriptableObject.CreateInstance<GameplayEffectDefinitionAsset>();a.name=id;JsonUtility.FromJsonOverwrite("{\"definitionId\":\""+id+"\"}",a);a.DurationPolicy=GameplayEffectDurationPolicy.Duration;a.Duration=3;return a;}
  [Test]public void EmptyCompletionRoundTripAndLegacyBuild()
  {var a=Asset();var b=Asset();try{JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(a),b);Assert.That(b.CompletionExecutions,Is.Empty);Assert.That(b.Build(new GameplayAuthoringBuildContext()).CompletionExecutions,Is.Empty);}finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}}
  [Test]public void ApplyCompletionUsesSameBuildIdentityAndSerializedReference()
  {var root=Asset();var next=Asset("Test.Completion.Next");try{root.CompletionExecutions=new GameplayEffectCompletionAuthoring[]{new GameplayApplyEffectOnCompletionAuthoring{Effect=next}};var context=new GameplayAuthoringBuildContext();var built=root.Build(context);Assert.That(((GameplayApplyEffectOnCompletionExecution)built.CompletionExecutions.Single()).Effect,Is.SameAs(next.Build(context)));var scan=GameplayReferenceScanner.ScanDefinitionReferences(new[]{root});Assert.That(scan.Single().Target,Is.SameAs(next));Assert.That(scan.Single().PropertyPath,Does.Contain("CompletionExecutions").And.Contain("Effect"));next.GrantedTags=new[]{new GameplayTagReference("Test.Completion.Nested")};Assert.That(GameplayReferenceScanner.ScanAssets(new[]{root}).Any(r=>r.Value=="Test.Completion.Nested"),Is.True);var catalog=ScriptableObject.CreateInstance<GameplayReferenceCatalogAsset>();try{var report=GameplayReferenceDiagnostics.Scan(catalog,new[]{root},Array.Empty<GameplayCodeLiteralReference>());Assert.That(report.Errors.Any(e=>e.Message.Contains("Test.Completion.Nested")),Is.True);}finally{UnityEngine.Object.DestroyImmediate(catalog);}}finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(next);}}
  [Test]public void CircularCompletionReferenceRejectsWithChildPath()
  {var root=Asset();var next=Asset("Test.Completion.Next");try{root.CompletionExecutions=new GameplayEffectCompletionAuthoring[]{new GameplayApplyEffectOnCompletionAuthoring{Effect=next}};next.CompletionExecutions=new GameplayEffectCompletionAuthoring[]{new GameplayApplyEffectOnCompletionAuthoring{Effect=root}};var v=new GameplayAuthoringValidationContext();v.Asset(root,"Root");Assert.That(v.Issues.Any(i=>i.Path=="Root.CompletionExecutions[0].Effect.CompletionExecutions[0].Effect"&&i.Message.Contains("Circular")),Is.True);Assert.That(GameplayReferenceScanner.ScanDefinitionReferences(new[]{root}).Count,Is.EqualTo(2));Assert.That(Assert.Throws<InvalidOperationException>(()=>root.Build(new GameplayAuthoringBuildContext())).Message,Does.Contain("CompletionExecutions[0].Effect"));}finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(next);}}
  [TestCase(GameplayEffectDurationPolicy.Instant)][TestCase(GameplayEffectDurationPolicy.Infinite)]public void NonDurationCompletionIsRejected(GameplayEffectDurationPolicy policy)
  {var root=Asset();var next=Asset();try{root.DurationPolicy=policy;root.CompletionExecutions=new GameplayEffectCompletionAuthoring[]{new GameplayApplyEffectOnCompletionAuthoring{Effect=next}};var v=new GameplayAuthoringValidationContext();v.Asset(root,"Root");Assert.That(v.Issues.Any(i=>i.Path=="Root.CompletionExecutions"),Is.True);Assert.Throws<InvalidOperationException>(()=>root.Build(new GameplayAuthoringBuildContext()));}finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(next);}}
  [Test]public void NullCompletionAndMissingEffectHavePrecisePaths()
  {var root=Asset();try{root.CompletionExecutions=new GameplayEffectCompletionAuthoring[]{null,new GameplayApplyEffectOnCompletionAuthoring()};var v=new GameplayAuthoringValidationContext();v.Asset(root,"Root");Assert.That(v.Issues.Select(i=>i.Path),Does.Contain("Root.CompletionExecutions[0]").And.Contain("Root.CompletionExecutions[1].Effect"));}finally{UnityEngine.Object.DestroyImmediate(root);}}
 }
}
