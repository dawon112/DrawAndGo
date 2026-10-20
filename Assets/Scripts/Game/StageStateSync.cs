using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Haru owns drawing. Dudu owns pickups and hazards so collisions use local physics.
// The host still owns the Relay session. This is a cooperative prototype, not anti-cheat.
[DisallowMultipleComponent]
public sealed class StageStateSync : MonoBehaviour
{
    private const string ReadyMessage = "StageReadyV1";
    private const string StrokeMessage = "StageStrokeV1";
    private const string WorldMessage = "StageWorldV1";
    private const string SkipMessage = "StageClearSkipV1";
    private const string EraseMessage = "StageEraseV1";
    private const int ChunkPoints = 16;
    private const float MaxRequestedEraseRadius = 1f;

    public static StageStateSync Active { get; private set; }
    private NetworkManager network;
    private CornerRoomLevel level;
    private DuduSurfaceMovement dudu;
    private DuduMovingObstacle[] movers;
    private MovingEraserObstacle[] erasers;
    private DuduHomingShooter[] shooters;
    private string layout;
    private bool ready;
    private ulong peer;
    private float nextSend;
    private Material remoteMaterial;
    private readonly Dictionary<int, int> sentPoints = new();
    private readonly Dictionary<int, DrawingStroke> remoteStrokes = new();
    private readonly Dictionary<int, Vector3> sentTranslation = new();
    private readonly Dictionary<int, Vector3> remoteTranslation = new();

    [Serializable]
    private sealed class StrokeDelta
    {
        public int id, offset;
        public float width, thickness, depth;
        public Color color;
        public Vector3 normal, translation;
        public Vector3[] points;
    }

    [Serializable]
    private struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool active;
        public static Pose Capture(Transform target) => target == null ? default : new Pose
        { position = target.position, rotation = target.rotation, active = target.gameObject.activeInHierarchy };
    }

    [Serializable]
    private sealed class WorldState
    {
        public int collected;
        public DuduSurfaceMovement.NetworkVisualState duduVisual;
        public Pose[] movers, erasers, projectiles;
        public bool doorOpen, clear;
    }

    private IEnumerator Start()
    {
        network = NetworkManager.Singleton;
        if (network == null || !network.IsConnectedClient)
        { enabled = false; yield break; }
        // Stage scripts (the tutorial) spawn hazards in their own Start. Wait one
        // frame so both peers index the same complete set of objects.
        yield return null;
        // CornerRoomLevel owns coins and the door; other stages (the tutorial) have none.
        level = FindAnyObjectByType<CornerRoomLevel>();
        dudu = FindAnyObjectByType<DuduSurfaceMovement>(FindObjectsInactive.Include);
        movers = FindObjectsByType<DuduMovingObstacle>();
        erasers = FindObjectsByType<MovingEraserObstacle>();
        shooters = FindObjectsByType<DuduHomingShooter>();
        Array.Sort(movers, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        Array.Sort(erasers, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        Array.Sort(shooters, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        layout = "stage-v2|" + SceneManager.GetActiveScene().name;
        if (level != null)
            foreach (var coin in level.coins) layout += "|coin:" + (coin != null ? Path(coin.transform) : "null");
        foreach (var mover in movers) layout += "|move:" + Path(mover.transform);
        foreach (var eraser in erasers) layout += "|eraser:" + Path(eraser.transform);
        foreach (var shooter in shooters) layout += "|shoot:" + Path(shooter.transform);
        network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReady);
        network.CustomMessagingManager.RegisterNamedMessageHandler(StrokeMessage, OnStroke);
        network.CustomMessagingManager.RegisterNamedMessageHandler(WorldMessage, OnWorld);
        network.CustomMessagingManager.RegisterNamedMessageHandler(SkipMessage, OnCinematicSkip);
        network.CustomMessagingManager.RegisterNamedMessageHandler(EraseMessage, OnEraseRequest);
        if (!network.IsHost)
        {
            var drawing = FindAnyObjectByType<HaruDrawingController>(FindObjectsInactive.Include);
            if (drawing == null) { Debug.LogError("Stage sync cannot find Haru drawing.", this); enabled = false; yield break; }
            remoteMaterial = drawing.CreateStrokeMaterialCopy();
        }
        Active = this;
    }

    // Client side: hazards simulated here ask the host to erase, which owns strokes.
    public void RequestErase(Vector3 start, Vector3 end, float radius)
    {
        if (network == null || network.IsHost || !ready || !network.IsConnectedClient) return;
        using var writer = new FastBufferWriter(sizeof(float) * 7, Allocator.Temp);
        writer.WriteValueSafe(start);
        writer.WriteValueSafe(end);
        writer.WriteValueSafe(radius);
        network.CustomMessagingManager.SendNamedMessage(
            EraseMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private void OnEraseRequest(ulong sender, FastBufferReader reader)
    {
        if (!network.IsHost || !ready || !ValidSender(sender) ||
            !reader.TryBeginRead(sizeof(float) * 7)) return;
        reader.ReadValueSafe(out Vector3 start);
        reader.ReadValueSafe(out Vector3 end);
        reader.ReadValueSafe(out float radius);
        radius = Mathf.Clamp(radius, 0.01f, MaxRequestedEraseRadius);
        // Push the result at once so the hole follows the eraser without a tick of delay.
        if (DrawingStroke.EraseAlongSegment(start, end, radius)) SendStrokes();
    }

    private static string Path(Transform target) => target.parent == null
        ? target.name : Path(target.parent) + "/" + target.name;

    // Coins are only ever collected (a restart reloads the scene), so OR-ing masks
    // from either side converges on one count without double-counting a coin.
    private int CollectedMask()
    {
        int mask = 0;
        if (level == null || level.coins == null) return mask;
        for (int i = 0; i < level.coins.Length && i < 31; i++)
            if (level.coins[i] == null || !level.coins[i].activeSelf) mask |= 1 << i;
        return mask;
    }

    private void ApplyCollectedMask(int mask)
    {
        if (level == null || level.coins == null) return;
        for (int i = 0; i < level.coins.Length && i < 31; i++)
            if ((mask & (1 << i)) != 0 && level.coins[i] != null) level.coins[i].SetActive(false);
    }

    private void LateUpdate()
    {
        if (layout == null || network == null || !network.IsConnectedClient || Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + 0.1f;
        if (!ready)
        {
            if (!network.IsHost) Send(ReadyMessage, NetworkManager.ServerClientId, layout);
            return;
        }
        if (network.IsHost)
        {
            SendStrokes();
            // The host's coin mask is the shared truth; the client hides any it missed.
            Send(WorldMessage, peer, JsonUtility.ToJson(level == null ? new WorldState() : new WorldState
            { collected = CollectedMask(), doorOpen = level.IsDoorVisuallyOpen, clear = level.IsClear }));
        }
        else
        {
            var state = new WorldState
            {
                movers = new Pose[movers.Length],
                erasers = new Pose[erasers.Length],
                projectiles = new Pose[shooters.Length]
            };
            if (dudu != null) state.duduVisual = dudu.CaptureNetworkVisual();
            state.collected = CollectedMask();
            for (int i = 0; i < movers.Length; i++) state.movers[i] = Pose.Capture(movers[i].transform);
            for (int i = 0; i < erasers.Length; i++) state.erasers[i] = Pose.Capture(erasers[i] != null ? erasers[i].transform : null);
            for (int i = 0; i < shooters.Length; i++) state.projectiles[i] = Pose.Capture(shooters[i].ActiveProjectile);
            Send(WorldMessage, NetworkManager.ServerClientId, JsonUtility.ToJson(state));
        }
    }

    private void OnReady(ulong sender, FastBufferReader reader)
    {
        if (!ValidSender(sender)) return;
        reader.ReadValueSafe(out string remoteLayout);
        if (remoteLayout != layout)
        {
            Debug.LogError("Stage layouts differ. Both players must use the same build.", this);
            return;
        }
        if (network.IsHost)
        {
            peer = sender;
            if (!ready) sentPoints.Clear();
            Send(ReadyMessage, peer, layout);
        }
        ready = true;
    }

    private bool ValidSender(ulong sender)
    {
        if (network == null) return false;
        if (!network.IsHost) return sender == NetworkManager.ServerClientId;
        return sender != NetworkManager.ServerClientId && network.ConnectedClients.ContainsKey(sender);
    }

    private void SendStrokes()
    {
        var strokes = new List<DrawingStroke>();
        foreach (var stroke in FindObjectsByType<DrawingStroke>())
            if (!stroke.IsErased && !stroke.IsNetworkMirror) strokes.Add(stroke);
        var alive = new HashSet<int>();
        foreach (var stroke in strokes) alive.Add(stroke.NetworkStrokeId);
        foreach (int id in new List<int>(sentPoints.Keys))
        {
            if (alive.Contains(id)) continue;
            Send(StrokeMessage, peer, JsonUtility.ToJson(new StrokeDelta { id = id, offset = -1 }));
            sentPoints.Remove(id);
            sentTranslation.Remove(id);
        }
        foreach (var stroke in strokes)
        {
            int id = stroke.NetworkStrokeId;
            bool known = sentPoints.TryGetValue(id, out int offset);
            sentTranslation.TryGetValue(id, out Vector3 lastTranslation);
            // Wind moves finished strokes on the host only. Send the running total so
            // the mirror replays the same rigid move, even when no point was added.
            bool moved = known && (stroke.TotalTranslation - lastTranslation).sqrMagnitude > 0.00000001f;
            var line = stroke.GetComponent<LineRenderer>();
            while (offset < stroke.PointCount || moved)
            {
                int count = Mathf.Min(ChunkPoints, stroke.PointCount - offset);
                var delta = new StrokeDelta
                {
                    id = id, offset = offset, width = line.startWidth, color = line.startColor,
                    thickness = stroke.ColliderThickness, depth = stroke.ColliderDepth,
                    normal = stroke.SurfaceNormal, translation = stroke.TotalTranslation,
                    points = new Vector3[count]
                };
                for (int i = 0; i < count; i++) delta.points[i] = line.GetPosition(offset + i);
                Send(StrokeMessage, peer, JsonUtility.ToJson(delta));
                offset += count;
                moved = false;
            }
            sentPoints[id] = offset;
            sentTranslation[id] = stroke.TotalTranslation;
        }
    }

    private void OnStroke(ulong sender, FastBufferReader reader)
    {
        if (network.IsHost || !ready || !ValidSender(sender)) return;
        reader.ReadValueSafe(out string json);
        var delta = JsonUtility.FromJson<StrokeDelta>(json);
        if (delta == null) return;
        if (delta.offset == -1)
        {
            remoteTranslation.Remove(delta.id);
            if (remoteStrokes.Remove(delta.id, out var removed) && removed != null)
            { removed.gameObject.SetActive(false); Destroy(removed.gameObject); }
            return;
        }
        if (delta.points == null || delta.points.Length > ChunkPoints) return;
        if (!remoteStrokes.TryGetValue(delta.id, out var stroke) || stroke == null)
        {
            if (delta.offset != 0 || delta.points.Length == 0) return;
            var holder = new GameObject("Remote Drawing Stroke");
            holder.transform.SetParent(transform, false);
            stroke = holder.AddComponent<DrawingStroke>();
            stroke.MarkAsNetworkMirror();
            int layer = LayerMask.NameToLayer("DrawnStroke");
            stroke.Initialize(remoteMaterial, delta.width, delta.color, delta.points[0],
                delta.thickness, delta.depth, delta.normal, layer >= 0 ? layer : 0);
            remoteStrokes[delta.id] = stroke;
            // Points arrive in the host's current (already moved) space.
            remoteTranslation[delta.id] = delta.translation;
            DuduCameraController.RegisterRuntimeRenderer(stroke.GetComponent<LineRenderer>());
        }
        remoteTranslation.TryGetValue(delta.id, out Vector3 applied);
        stroke.Translate(delta.translation - applied);
        remoteTranslation[delta.id] = delta.translation;
        // Reliable ordered deltas append only new points; existing colliders remain intact.
        if (delta.offset > stroke.PointCount) return;
        for (int i = Mathf.Max(0, stroke.PointCount - delta.offset); i < delta.points.Length; i++)
            stroke.AddPoint(delta.points[i]);
    }

    private void OnWorld(ulong sender, FastBufferReader reader)
    {
        if (!ready || !ValidSender(sender)) return;
        reader.ReadValueSafe(out string json);
        var state = JsonUtility.FromJson<WorldState>(json);
        if (state == null) return;
        if (!network.IsHost)
        {
            if (level == null) return;
            ApplyCollectedMask(state.collected);
            if (state.doorOpen) level.OpenDoorForCinematic();
            if (state.clear) level.ApplyRemoteClear();
            return;
        }
        if (state.movers == null || state.movers.Length != movers.Length ||
            state.erasers == null || state.erasers.Length != erasers.Length ||
            state.projectiles == null || state.projectiles.Length != shooters.Length) return;
        if (dudu != null) dudu.ApplyNetworkVisual(state.duduVisual);
        ApplyCollectedMask(state.collected);
        for (int i = 0; i < movers.Length; i++)
            movers[i].ApplyRemotePose(state.movers[i].position, state.movers[i].rotation);
        for (int i = 0; i < erasers.Length; i++)
            erasers[i].ApplyRemotePose(state.erasers[i].active,
                state.erasers[i].position, state.erasers[i].rotation);
        for (int i = 0; i < shooters.Length; i++)
            shooters[i].ApplyRemoteProjectile(state.projectiles[i].active,
                state.projectiles[i].position, state.projectiles[i].rotation);
    }

    public void BroadcastCinematicSkip()
    {
        if (network == null || !network.IsConnectedClient || !ready) return;
        if (network.IsHost)
        {
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != network.LocalClientId) SendCinematicSkip(clientId);
        }
        else
            SendCinematicSkip(NetworkManager.ServerClientId);
    }

    private void OnCinematicSkip(ulong sender, FastBufferReader reader)
    {
        if (!ready || !ValidSender(sender) || !reader.TryBeginRead(sizeof(byte))) return;
        reader.ReadValueSafe(out byte requested);
        if (requested == 0) return;
        FindAnyObjectByType<StageCinematicController>()?.RequestNetworkClearSkip();

        // Relay a client's request through the host so every connected view skips.
        if (!network.IsHost) return;
        foreach (ulong clientId in network.ConnectedClientsIds)
            if (clientId != network.LocalClientId && clientId != sender)
                SendCinematicSkip(clientId);
    }

    private void SendCinematicSkip(ulong destination)
    {
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        network.CustomMessagingManager.SendNamedMessage(
            SkipMessage, destination, writer, NetworkDelivery.ReliableSequenced);
    }

    private void Send(string name, ulong destination, string payload)
    {
        using var writer = new FastBufferWriter(8 + payload.Length * 2, Allocator.Temp);
        writer.WriteValueSafe(payload);
        network.CustomMessagingManager.SendNamedMessage(name, destination, writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void OnDestroy()
    {
        if (network != null && network.CustomMessagingManager != null)
        {
            network.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(StrokeMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(WorldMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(SkipMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(EraseMessage);
        }
        if (Active == this) Active = null;
        if (remoteMaterial != null) Destroy(remoteMaterial);
    }
}
