using Cysharp.Threading.Tasks;
using Fusion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UniRx;
using UnityEngine;

namespace Generic
{
    public static class PhotonFusionEx
    {
        #region Tick

        public static int ToTick(this int second, NetworkRunner runner) => (int)(second / runner.DeltaTime);
        public static int ToTick(this float second, NetworkRunner runner) => (int)(second / runner.DeltaTime);
        public static float ToSecond(this int tick, NetworkRunner runner) => tick * runner.DeltaTime;

        public static int GetTickAfter(this NetworkRunner runner, float second) => runner.Tick + (int)(second / runner.DeltaTime);

        public static float ElapsedTime(this NetworkRunner runner, int tick) => (runner.Tick - tick) * runner.DeltaTime;
        public static float RemainingTime(this NetworkRunner runner, int tick) => -runner.ElapsedTime(tick);

        public static bool IsAt(this NetworkRunner runner, int tick) => runner.Tick == tick;
        public static bool HasPassed(this NetworkRunner runner, int tick) => (runner.Tick - tick) > 0;
        public static bool HasntPassed(this NetworkRunner runner, int tick) => (runner.Tick - tick) <= 0;
        public static bool HasReached(this NetworkRunner runner, int tick) => (runner.Tick - tick) >= 0;
        public static bool HasntReached(this NetworkRunner runner, int tick) => (runner.Tick - tick) < 0;

        public static double SimulationRenderTime(this NetworkRunner runner, int offsetTick = 0)
            => runner.Simulation.StatePrevious.Tick - offsetTick + runner.Simulation.StateAlpha * runner.Simulation.DeltaTime;
        public static double InterpolationRenderTime(this NetworkRunner runner, int offsetTick = 0)
            => (runner.IsServer ? runner.Tick : runner.Simulation.InterpFrom.Tick) - offsetTick + runner.Simulation.InterpAlpha * runner.Simulation.DeltaTime;
        public static float InterpolationSecond(this NetworkRunner runner)
            => runner.IsServer ? 0f : (runner.Simulation.InterpTo.Tick - runner.Simulation.InterpFrom.Tick) * runner.DeltaTime;

        #endregion

        #region TickTimer

        public static IObservable<Unit> OnCompleted(this TickTimer tickTimer, NetworkRunner runner)
            => Observable.EveryUpdate()
                .Select(_ => tickTimer.Expired(runner))
                .DistinctUntilChanged()
                .Where(completed => completed)
                .Select(_ => Unit.Default)
                .Publish().RefCount();

        public static IObservable<Unit> OnUpdated(this TickTimer tickTimer, NetworkRunner runner)
            => Observable.EveryUpdate().Where(_ => !tickTimer.Expired(runner)).Select(_ => Unit.Default)
                .Publish().RefCount();

        public static IObservable<float> OnRunnerUpdated(this TickTimer tickTimer, NetworkRunner runner)
            => Observable.EveryUpdate()
                .Where(_ => !tickTimer.Expired(runner))
                .Select(_ => tickTimer.RemainingTime(runner).Value)
                .DistinctUntilChanged()
                .Publish().RefCount();

        public static IObservable<int> OnCountDowned(this TickTimer tickTimer, NetworkRunner runner, int minCount = 1, int maxCount = int.MaxValue)
            => Observable.EveryUpdate()
                .Where(_ => !tickTimer.Expired(runner))
                .Select(_ => Mathf.CeilToInt(tickTimer.RemainingTime(runner).Value))
                .DistinctUntilChanged()
                .Where(count => count >= minCount && count <= maxCount)
                .Publish().RefCount();

        #endregion

        #region Network Array, LinkedList, Dictionary

        public static void ForEach<T>(this NetworkArray<T> array, Action<T> action)
        {
            foreach (var item in array) action.Invoke(item);
        }
        public static void ForLoop<T>(this NetworkArray<T> array, Action<int, T> action)
        {
            for (int i = 0; i < array.Length; i++) action.Invoke(i, array[i]);
        }
        public static void ForLoop<T>(this NetworkArray<T> array, NetworkArray<T> oldArray, Action<int, T, T> action)
        {
            for (int i = 0; i < array.Length; i++) action.Invoke(i, oldArray[i], array[i]);
        }
        public static void Replace<T>(this NetworkArray<T> array, Func<T, bool> conditions, Func<T, T> value)
        {
            for (int i = 0; i < array.Length; i++) if (conditions.Invoke(array.Get(i))) array.Set(i, value.Invoke(array.Get(i)));
        }
        public static void Replace<T>(this NetworkArray<T> array, Func<T, bool> conditions, T value) => Replace(array, conditions, v => value);
        public static int ReplaceOne<T>(this NetworkArray<T> array, Func<T, bool> conditions, Func<T, T> value)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (conditions.Invoke(array.Get(i)))
                {
                    array.Set(i, value.Invoke(array.Get(i)));
                    return i;
                }
            }
            return -1;
        }
        public static int ReplaceOne<T>(this NetworkArray<T> array, Func<T, bool> conditions, T value) => ReplaceOne(array, conditions, v => value);
        public static bool ReplaceOneByOne<T>(this NetworkArray<T> array, Func<T, bool> conditions, params T[] values)
        {
            var index = 0;
            for (int i = 0; i < array.Length; i++)
            {
                if (conditions.Invoke(array.Get(i)))
                {
                    array.Set(i, values[index++]);
                    if (values.Length == index) return true;
                }
            }
            return false;
        }
        public static void ReplaceAll<T>(this NetworkArray<T> array, Func<T, T> value)
        {
            for (int i = 0; i < array.Length; i++) array.Set(i, value.Invoke(array.Get(i)));
        }
        public static void ReplaceAll<T>(this NetworkArray<T> array, T value) => ReplaceAll(array, v => value);

        public static void OnValueChanged<Class, T>(this NetworkArray<T> currentArray, Changed<Class> changed, Func<Class, NetworkArray<T>> loadArray, Action<int, T, T> action)
            where Class : NetworkBehaviour
            where T : IEquatable<T>
        {
            var prevArray = new NetworkArray<T>();
            changed.LoadOld(old => prevArray = loadArray.Invoke(old));

            for (int i = 0; i < prevArray.Length; i++)
            {
                if (!currentArray[i].Equals(prevArray[i])) action?.Invoke(i, prevArray[i], currentArray[i]);
            }
        }

        public static void CopyFrom<T>(this NetworkArray<T> netArray, T[] array) => netArray.CopyFrom(array, 0, array.Length - 1);

        #endregion

        #region Network LinkedList, Dictionary

        public static void CopyFrom<T>(this NetworkLinkedList<T> netList, List<T> list)
        {
            if (list.Count == netList.Count)
            {
                for (int i = 0; i < list.Count; i++) netList.Set(i, list[i]);
            }
            else
            {
                netList.Clear();
                for (int i = 0; i < list.Count; i++) netList.Add(list[i]);
            }
        }

        public static void CopyFrom<K, V>(this NetworkDictionary<K, V> netDict, KeyValuePair<K, V>[] pairs)
        {
            netDict.Clear();
            for (int i = 0; i < pairs.Length; i++) netDict.Add(pairs[i].Key, pairs[i].Value);
        }

        #endregion

        #region PlayerRef

        public static PlayerRef Host(this NetworkRunner runner) => runner.GameMode == GameMode.Server ? PlayerRef.None : runner.Simulation.Config.DefaultPlayers - 1;
        public static bool IsServerMode(this NetworkRunner runner) => runner.GameMode == GameMode.Server;

        public static bool IsHost(this PlayerRef playerRef, NetworkRunner runner) => playerRef == Host(runner);
        public static bool IsMe(this PlayerRef playerRef, NetworkRunner runner) => playerRef == runner.LocalPlayer;

        public static bool HasInputAuthorityTo(this PlayerRef playerRef, NetworkObject no) => playerRef == no.InputAuthority;
        public static bool HasStateAuthorityTo(this PlayerRef playerRef, NetworkObject no) => playerRef == no.StateAuthority;
        public static bool HasInputAuthorityTo(this PlayerRef playerRef, NetworkBehaviour nb) => playerRef == nb.Object.InputAuthority;
        public static bool HasStateAuthorityTo(this PlayerRef playerRef, NetworkBehaviour nb) => playerRef == nb.Object.StateAuthority;

        #endregion

        #region NetworkBehaviour, NetworkObject

        public static bool IsStatedByMe(this NetworkObject no) => no.StateAuthority == no.Runner.LocalPlayer;
        public static bool IsInputtedByMe(this NetworkObject no) => no.InputAuthority == no.Runner.LocalPlayer;
        public static bool IsStatedByMe(this NetworkBehaviour nb) => nb.Object.StateAuthority == nb.Runner.LocalPlayer;
        public static bool IsInputtedByMe(this NetworkBehaviour nb) => nb.Object.InputAuthority == nb.Runner.LocalPlayer;

        public static int GetSeed(this NetworkRunner runner) => runner.SessionInfo.Properties["seed"];
        public static int GetSeed(this NetworkBehaviour nb) => unchecked((int)nb.Runner.SessionInfo.Properties["seed"] + nb.Id.Behaviour);

        #endregion


        #region Other

        /// <summary>
        /// Normally RpcInfo.Source will be None when Host/Server calls RPC.
        /// This extension method makes the Host's PlayerRef available when the Host calls an RPC.
        /// </summary>
        public static PlayerRef Source(this RpcInfo info, NetworkRunner runner) => info.Source.IsNone ? runner.Host() : info.Source;

        public static void LoadOld<T>(this Changed<T> changed, Action<T> old) where T : NetworkBehaviour
        {
            changed.LoadOld();
            old?.Invoke(changed.Behaviour);
            changed.LoadNew();
        }
        public static T2 LoadOld<T, T2>(this Changed<T> changed, Func<T, T2> old) where T : NetworkBehaviour
        {
            changed.LoadOld();
            var v = old.Invoke(changed.Behaviour);
            changed.LoadNew();
            return v;
        }

        public static T FindBehaviour<T>(this NetworkRunner runner) where T : SimulationBehaviour 
            => runner.GetAllBehaviours<T>().FirstOrDefault();
        public static bool FindBehaviour<T>(this NetworkRunner runner, out T behaviour) where T : SimulationBehaviour
        {
            behaviour = runner.GetAllBehaviours<T>().FirstOrDefault();
            return behaviour != null;
        }
        public static async UniTask<T> FindBehaviourAsync<T>(this NetworkRunner runner, CancellationToken token) where T : SimulationBehaviour
        {
            while (token.IsCancellationRequested == false)
            {
                if (runner.FindBehaviour<T>(out var behaviour)) return behaviour;
                await UniTask.DelayFrame(1);
            }
            return default;
        }

        public static T FindBehaviour<T>(this NetworkRunner runner, PlayerRef player) where T : SimulationBehaviour
            => runner.GetAllBehaviours<T>().FirstOrDefault(b => b.Object.InputAuthority == player);
        public static bool FindBehaviour<T>(this NetworkRunner runner, PlayerRef player, out T behaviour) where T : SimulationBehaviour
        {
            behaviour = runner.FindBehaviour<T>(player);
            return behaviour != null;
        }
        public static async UniTask<T> FindBehaviourAsync<T>(this NetworkRunner runner, PlayerRef player, CancellationToken token) where T : SimulationBehaviour
        {
            while (token.IsCancellationRequested == false)
            {
                if (runner.FindBehaviour<T>(player, out var behaviour)) return behaviour;
                await UniTask.DelayFrame(1);
            }
            return default;
        }

        public static void Disconnects(this NetworkRunner runner, IEnumerable<PlayerRef> targetPlayers)
        {
            foreach (var p in targetPlayers) if (runner.ActivePlayers.Contains(p)) runner.Disconnect(p);
        }

        public static bool TryAssignInputAuthority(this NetworkObject no, Guid objectToken, bool noAssignment = true)
        {
            foreach (var p in no.Runner.ActivePlayers)
            {
                if (new Guid(no.Runner.GetPlayerConnectionToken(p)) != objectToken) continue;
                no.AssignInputAuthority(p);
                return true;
            }
            if (noAssignment) no.AssignInputAuthority(PlayerRef.None);
            return false;
        }
        #endregion
    }

    public static class PhotonFusionUtil
    {
        public static NetworkRunner Runner => NetworkRunner.Instances.FirstOrDefault(r => r != null);
        public static bool TryGetRunner(out NetworkRunner runner)
        {
            runner = Runner;
            return runner != null;
        }

        /// <summary> This function simplifies the implementation of firing multiple processes with one Tick. </summary>
        public static void FiringActionsWithStartTick(int elapsedTick, Action<int> action, params int[] timingTick)
        {
            for (int i = 0; i < timingTick.Length; i++) if (elapsedTick == timingTick[i]) action?.Invoke(i);
        }
        /// <summary> This function simplifies the implementation of firing multiple processes with one Tick. </summary>
        public static void FiringActionsWithStartTick(int elapsedTick, int[] timingTick, params Action<int>[] actions)
        {
            for (int i = 0; i < timingTick.Length; i++) if (elapsedTick == timingTick[i]) actions[i]?.Invoke(i);
        }
        /// <summary> This function simplifies the implementation of firing multiple processes with one Tick. </summary>
        public static void FiringActionsWithStartTick(NetworkRunner runner, int startTick, int[] timingTicks, params Action<int>[] actions)
            => FiringActionsWithStartTick(runner.Tick - startTick, timingTicks, actions);
        /// <summary> This function simplifies the implementation of firing multiple processes with one Tick. </summary>
        public static void FiringActionsWithStartTick(NetworkRunner runner, int startTick, Action<int> action, params int[] timingTicks)
            => FiringActionsWithStartTick(runner.Tick - startTick, action, timingTicks);
        /// <summary> This function simplifies the implementation of firing multiple processes with one Tick. </summary>
        public static void FiringActionsWithEndTick(NetworkRunner runner, int endTick, int durationTick, int[] timingTicks, params Action<int>[] actions)
            => FiringActionsWithStartTick(runner.Tick - endTick + durationTick, timingTicks, actions);
        /// <summary> This function simplifies the implementation of firing multiple processes with one Tick. </summary>
        public static void FiringActionsWithEndTick(NetworkRunner runner, int endTick, int lengthToEndTick, Action<int> action, params int[] timingTicks)
            => FiringActionsWithStartTick(runner.Tick - endTick + lengthToEndTick, action, timingTicks);


        /// <summary> This function simplifies the implementation of sequentially executing processes with one Tick. </summary>
        public static void UpdateFlowByStart(int elapsedTick, int[] durationTicks, params Action<int>[] actions)
        {
            if (elapsedTick >= durationTicks.Last()) return;
            for (int i = 0; i < durationTicks.Length; i++)
            {
                if (elapsedTick >= durationTicks[i]) continue;
                actions[i]?.Invoke(i == 0 ? elapsedTick : elapsedTick - durationTicks[i - 1]);
                return;
            }
        }
        /// <summary> This function simplifies the implementation of sequentially executing processes with one Tick. </summary>
        public static void UpdateFlowByStart(int elapsedTick, Action<(int index, int elapsedTick)> action, params int[] durationTicks)
        {
            if (elapsedTick >= durationTicks.Last()) return;
            for (int i = 0; i < durationTicks.Length; i++)
            {
                if (elapsedTick >= durationTicks[i]) continue;
                action?.Invoke((i, i == 0 ? elapsedTick : elapsedTick - durationTicks[i - 1]));
                return;
            }
        }
        /// <summary> This function simplifies the implementation of sequentially executing processes with one Tick. </summary>
        public static void UpdateFlowByStart(NetworkRunner runner, int startTick, int[] durationTicks, params Action<int>[] actions)
            => UpdateFlowByStart(runner.Tick - startTick, durationTicks, actions);
        /// <summary> This function simplifies the implementation of sequentially executing processes with one Tick. </summary>
        public static void UpdateFlowByStart(NetworkRunner runner, int startTick, Action<(int index, int elapsedTick)> action, params int[] durationTicks)
            => UpdateFlowByStart(runner.Tick - startTick, action, durationTicks);
        /// <summary> This function simplifies the implementation of sequentially executing processes with one Tick. </summary>
        public static void UpdateFlowByComplete(NetworkRunner runner, int completeTick, int[] durationTicks, params Action<int>[] actions)
        {
            if (runner.Tick > completeTick) return;
            UpdateFlowByStart(runner.Tick - completeTick + durationTicks.Last(), durationTicks, actions);
        }
        /// <summary> This function simplifies the implementation of sequentially executing processes with one Tick. </summary>
        public static void UpdateFlowByComplete(NetworkRunner runner, int completeTick, Action<(int index, int elapsedTick)> action, params int[] durationTicks)
        {
            if (runner.Tick > completeTick) return;
            UpdateFlowByStart(runner.Tick - completeTick + durationTicks.Last(), action, durationTicks);
        }
    }
}
