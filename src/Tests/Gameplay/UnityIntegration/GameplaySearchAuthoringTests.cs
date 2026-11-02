using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Editor
{
    public sealed class GameplaySearchAuthoringTests
    {
        /// <summary>Protects shape switching: dormant broken drafts cannot block/build into an
        /// unrelated query, but become validation errors when their consuming shape is selected.</summary>
        [Test]
        public void InactiveDraftsSurviveShapeSwitchWithoutBlockingBuild()
        {
            var radius=new GameplayConstantFloatValueAuthoring{Value=0};
            var search=new GameplaySearchTargetActionAuthoring{Shape=GameplaySearchShape.Single,Radius=radius,Angle=null,Length=null,Width=null,
                DirectionAngle=new GameplayConstantFloatValueAuthoring{Value=float.NaN},LengthFromTarget=true,CellSize=0,Cells=null};
            var validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");Assert.That(validation.HasErrors,Is.False);
            Assert.DoesNotThrow(()=>search.Build(new GameplayAuthoringBuildContext()));
            search.Shape=GameplaySearchShape.Circle;validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");Assert.That(validation.HasErrors,Is.True);
            Assert.That(search.Radius,Is.SameAs(radius));radius.Value=3;
            validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");Assert.That(validation.HasErrors,Is.False);
            foreach(GameplaySpatialPlane plane in System.Enum.GetValues(typeof(GameplaySpatialPlane))){
                search.Plane=plane;validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");
                Assert.That(validation.HasErrors,Is.False);Assert.DoesNotThrow(()=>search.Build(new GameplayAuthoringBuildContext()));
            }
            search.Shape=GameplaySearchShape.GridCells;search.Cells=new[]{UnityEngine.Vector3.zero};search.CellSize=1;
            validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");Assert.That(validation.HasErrors,Is.False);
            Assert.DoesNotThrow(()=>search.Build(new GameplayAuthoringBuildContext()));
            search.Shape=GameplaySearchShape.Capsule;validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");Assert.That(validation.HasErrors,Is.True);
            search.Direction=GameplaySearchDirection.ToContextPoint;search.DirectionAngle=new GameplayConstantFloatValueAuthoring{Value=0};
            validation=new GameplayAuthoringValidationContext();search.Validate(validation,"Search");Assert.That(validation.HasErrors,Is.False,"Endpoint length does not consume the missing Length draft.");
            Assert.DoesNotThrow(()=>search.Build(new GameplayAuthoringBuildContext()));
        }
    }
}
