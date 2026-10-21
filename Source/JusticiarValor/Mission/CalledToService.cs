using RimWorld;
using Verse;
using Verse.AI;

namespace JusticiarValor
{
    // A convert who has heard the call to serve the Empire walks to the missionary's shuttle and boards it. Like a
    // colonist giving up and leaving, they cannot be controlled, but can be arrested on the way.
    public class MentalState_MJV_CalledToService : MentalState
    {
        public Thing shuttle;

        // The call comes as the shuttle is announced; it may still be landing (held by its skyfaller), so only a shuttle
        // that is gone or on another map ends it.
        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (pawn.IsHashIntervalTick(60, delta) && (shuttle == null || shuttle.Destroyed || shuttle.MapHeld != pawn.Map))
            {
                RecoverFromState();
            }
        }

        public override RandomSocialMode SocialModeMax() => RandomSocialMode.Off;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref shuttle, "shuttle");
        }
    }

    // Think tree (inserted at Humanlike_PostMentalState) of MJV_CalledToService: board the shuttle.
    public class JobGiver_MJV_BoardImperialShuttle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn.MentalState is MentalState_MJV_CalledToService state) || state.shuttle == null || !state.shuttle.Spawned
                || state.shuttle.Map != pawn.Map || !pawn.CanReach(state.shuttle, PathEndMode.Touch, Danger.Deadly))
            {
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.EnterTransporter, state.shuttle);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            return job;
        }
    }
}
