using Unity.Netcode;
using UnityEngine;

public partial class MatchManager
{
    public const float CaptureTransitionSeconds = 3.2f;
    const float CaptureTeleportDelay = .9f; // fully black by .5 seconds
    public NetworkVariable<CapturePresentationState> capturePresentation = new NetworkVariable<CapturePresentationState>();
    public NetworkVariable<double> capturePauseUntil = new NetworkVariable<double>();
    public double CaptureClock => NetworkManager != null && IsSpawned ? NetworkManager.ServerTime.Time : 0;
    public bool CaptureCombatPaused => IsAttackMode && !IsLobby && !IsOver &&
        (roundPhase.Value==RoundIntermission || roundPhase.Value==RoundDone
        || capturePresentation.Value.Active(CaptureClock) || CaptureClock<capturePauseUntil.Value);
    float CapturePauseRemaining => (float)System.Math.Max(0,System.Math.Max(capturePauseUntil.Value,
        capturePresentation.Value.started+capturePresentation.Value.duration)-CaptureClock);

    enum CaptureAction { None, NextPoint, EndRound, BeginRound, CutsceneTeleport }
    CaptureAction captureAction;
    double captureActionAt;
    int pendingPoint;
    float pendingProgress;

    void ServerResetCaptureTransition()
    {
        captureAction=CaptureAction.None;
        capturePresentation.Value=default;
        capturePauseUntil.Value=0;
        cutsceneTeleportAt=-1;
    }

    void ServerPresentCapture(int kind,int team,int point,int next,float duration,CaptureAction action,float delay)
    {
        capturePresentation.Value=new CapturePresentationState { kind=kind,team=team,point=point,next=next,
            started=CaptureClock,duration=duration };
        captureAction=action;
        captureActionAt=CaptureClock+delay;
        pointState.Value=StateLocked;
    }

    bool ServerCaptureTransitionTick()
    {
        if (captureAction!=CaptureAction.None && CaptureClock>=captureActionAt)
        {
            var action=captureAction;
            captureAction=CaptureAction.None;
            switch(action)
            {
                case CaptureAction.NextPoint: ServerAdvanceCapturedPoint(pendingPoint); break;
                case CaptureAction.EndRound: ServerFinishRound(pendingPoint,pendingProgress); break;
                case CaptureAction.BeginRound: ServerCompleteNextRound(); break;
                case CaptureAction.CutsceneTeleport:
                    ResetPlayersClientRpc(customSpawns,customSpawn[0],customSpawn[1]);
                    ServerHoldUntilUnlock();
                    break;
            }
        }
        return captureAction!=CaptureAction.None || capturePresentation.Value.Active(CaptureClock)
            || CaptureClock<capturePauseUntil.Value;
    }

    void ServerQueueCapturedPoint(int next)
    {
        pendingPoint=next;
        pendingProgress=0;
        bool end=next>=CapturePointCount || (roundKind.Value==KindSecond && Score(attackTeam.Value)>Score(DefendTeam))
            || roundKind.Value==KindSolo;
        bool film=false;
        if (!end)
            foreach(var floor in FindObjectsByType<FoldingFloor>(FindObjectsSortMode.None))
                film |= floor.unlockAfter==next && floor.HasCutscene;
        if (film)
        {
            // The authored film is the result presentation. Start it immediately;
            // only its ending gets a fade to cover the move to the next spawn.
            capturePresentation.Value=default;
            captureAction=CaptureAction.None;
            ServerAdvanceCapturedPoint(next);
            return;
        }
        ServerPresentCapture(CapturePresentationState.Taken,attackTeam.Value,pointIndex.Value,end?-1:next,
            CaptureTransitionSeconds,end?CaptureAction.EndRound:CaptureAction.NextPoint,
            end ? CaptureTransitionSeconds : CaptureTeleportDelay);
    }

    void ServerAdvanceCapturedPoint(int next)
    {
        timer += pointIndex.Value<2 ? BonusSeconds : LateBonusSeconds;
        if (roundPhase.Value==RoundOvertime) roundPhase.Value=RoundAttack;
        overtimeEmpty=0;
        float film=ServerStartCutscene(next);
        if (film>0) capturePauseUntil.Value=CaptureClock+film;
        ServerApplyPoint(next,UnlockNextSeconds);
        if (film<=0)
        {
            ResetPlayersClientRpc(customSpawns,customSpawn[0],customSpawn[1]);
            ServerHoldUntilUnlock();
        }
    }

    void ServerEndRound(int pointsTaken,float progressOnNext)
    {
        pendingPoint=pointsTaken;
        pendingProgress=progressOnNext;
        bool goal=timer>0;
        ServerPresentCapture(goal?CapturePresentationState.GoalReached:CapturePresentationState.Defended,
            goal?attackTeam.Value:DefendTeam,pointIndex.Value,-1,CaptureTransitionSeconds,
            CaptureAction.EndRound,CaptureTransitionSeconds);
    }

    void ServerBeginNextRound() => ServerPresentCapture(CapturePresentationState.SwitchSides,nextAttacker,
        nextAttacker==attackTeam.Value?-1:0,0,CaptureTransitionSeconds,CaptureAction.BeginRound,CaptureTeleportDelay);
}
