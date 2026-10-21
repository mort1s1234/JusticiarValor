using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    // Duty MJV_DeliverSermon: go to the sermon spot (the duty's focus) and preach there.
    public class JobGiver_MJV_DeliverSermon : ThinkNode_JobGiver
    {
        public SoundDef soundDefMale;
        public SoundDef soundDefFemale;

        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null || !duty.focus.IsValid)
            {
                return null;
            }
            IntVec3 spot = duty.focus.Cell;
            IntVec3 stand = spot;
            if (!pawn.CanReserveAndReach(stand, PathEndMode.OnCell, Danger.Some))
            {
                Map map = pawn.Map;
                if (!CellFinder.TryFindRandomReachableNearbyCell(spot, map, 2f, TraverseParms.For(pawn, Danger.Some),
                    c => c.Standable(map) && pawn.CanReserve(c), null, out stand))
                {
                    return null;
                }
            }
            Job job = JobMaker.MakeJob(MJV_DefOf.MJV_PreachSermon, stand, spot);
            job.speechSoundMale = soundDefMale;
            job.speechSoundFemale = soundDefFemale;
            return job;
        }

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            var copy = (JobGiver_MJV_DeliverSermon)base.DeepCopy(resolve);
            copy.soundDefMale = soundDefMale;
            copy.soundDefFemale = soundDefFemale;
            return copy;
        }
    }

    // The preacher at work: speaks facing his congregation, with the imperial halo at his feet.
    public class JobDriver_MJV_DeliverSermon : JobDriver, IFxDrawer
    {
        private const int BubbleIntervalTicks = 480;
        private const int FacingIntervalTicks = 30;
        private const float HaloSize = 5.2f;
        private const float FadeInSeconds = 1.6f;

        private int ticksToBubble = 90;
        private int ticksToFacing;
        private IntVec3 facingCell = IntVec3.Invalid;
        private bool onStage;

        // Visuals only, not saved.
        private double stageStart = -1;
        private float seed = -1f;
        private Map fxMap;

        public bool OnStage => onStage;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.mindState.duty?.def != MJV_DefOf.MJV_DeliverSermon);
            AddFinishAction(_ => FadeOut());
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil preach = ToilMaker.MakeToil("MJV_DeliverSermon");
            preach.initAction = () => onStage = true;
            preach.tickIntervalAction = delta =>
            {
                EnsureFx();
                pawn.GainComfortFromCellIfPossible(delta);
                pawn.skills?.Learn(SkillDefOf.Social, 0.3f * delta);
                ticksToBubble -= delta;
                if (ticksToBubble <= 0)
                {
                    MoteMaker.MakeSpeechBubble(pawn, JobDriver_GiveSpeech.moteIcon);
                    ticksToBubble = BubbleIntervalTicks;
                }
                ticksToFacing -= delta;
                if (ticksToFacing <= 0 || !facingCell.IsValid)
                {
                    facingCell = CrowdCenter();
                    ticksToFacing = FacingIntervalTicks;
                }
                pawn.rotationTracker.FaceCell(facingCell);
            };
            preach.defaultCompleteMode = ToilCompleteMode.Never;
            preach.handlingFacing = true;
            preach.socialMode = RandomSocialMode.Off;
            preach.PlaySustainerOrSound(() => pawn.gender == Gender.Female ? job.speechSoundFemale : job.speechSoundMale, pawn.story?.VoicePitchFactor ?? 1f);
            yield return preach;
        }

        // Where the listeners are: the preacher turns to face them.
        private IntVec3 CrowdCenter()
        {
            Lord sermon = SermonUtility.SermonOf(pawn)?.lord;
            float x = 0f;
            float z = 0f;
            int count = 0;
            if (sermon != null)
            {
                List<Pawn> pawns = sermon.ownedPawns;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p != pawn && p.Spawned && p.Position.InHorDistOf(pawn.Position, 14f))
                    {
                        x += p.Position.x;
                        z += p.Position.z;
                        count++;
                    }
                }
            }
            if (count == 0)
            {
                return pawn.Position + IntVec3.South * 3;
            }
            var center = new IntVec3((int)(x / count + 0.5f), 0, (int)(z / count + 0.5f));
            return center == pawn.Position ? pawn.Position + IntVec3.South : center;
        }

        private void EnsureFx()
        {
            Map map = pawn.Map;
            if (map != null && fxMap != map)
            {
                fxMap = map;
                if (stageStart < 0)
                {
                    stageStart = FxClock.Now;
                }
                MapComponent_JusticiarFx.Register(map, this);
            }
        }

        private void FadeOut()
        {
            if (onStage && pawn.Spawned && stageStart >= 0)
            {
                float shown = JVMath.Clamp01((float)(FxClock.Now - stageStart) / FadeInSeconds);
                MapComponent_JusticiarFx.Spawn(pawn.Map, new SermonHaloFadeFx(pawn.DrawPos, HaloSize, seed < 0f ? 0f : seed, shown));
            }
            onStage = false;
            fxMap = null;
        }

        public bool FxActiveOn(Map map)
        {
            return onStage && fxMap == map && pawn.Spawned && pawn.Map == map && pawn.jobs?.curDriver == this;
        }

        public void DrawFx(FxDrawContext ctx)
        {
            if (seed < 0f)
            {
                seed = FxDraw.Seed();
            }
            float shown = JVMath.Clamp01((float)(ctx.now - stageStart) / FadeInSeconds);
            Vector3 pos = JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow);
            FxDraw.Quad(ctx, FxAssets.SermonHalo, pos, 0f, HaloSize, HaloSize, JVMath.White,
                JVMath.V4(0f, seed, FxClock.Wrapped(ctx.now), shown), JVMath.Zero4);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksToBubble, "ticksToBubble", 0);
            Scribe_Values.Look(ref ticksToFacing, "ticksToFacing", 0);
            Scribe_Values.Look(ref facingCell, "facingCell", IntVec3.Invalid);
            Scribe_Values.Look(ref onStage, "onStage", false);
        }
    }
}
