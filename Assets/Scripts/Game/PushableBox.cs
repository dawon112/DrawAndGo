using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// A crate Haru can shove by walking into it. It slides along the floor and drops through
// holes like anything else. Online, the host's crate is the real one: the host simulates
// every crate and sends their poses, and the client's copies just follow.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PushableBox : MonoBehaviour
{
    private const string PoseMessage = "PushBoxV1";
    private const float SendInterval = 0.08f;

    [Tooltip("How fast the crate slides while Haru keeps pushing it.")]
    [Min(0.1f)] public float pushSpeed = 2.4f;

    private static readonly List<PushableBox> boxes = new List<PushableBox>();
    private static NetworkManager registeredWith;
    private static float nextSendTime;

    private Rigidbody body;
    private bool following;
    private Vector3 remotePosition;
    private Quaternion remoteRotation;
    private bool hasRemotePose;
    private float lastPushTime = -1f;

    public Rigidbody Body => body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        boxes.Add(this);
        // Same order on both PCs: the hierarchy path, identical in the same build.
        boxes.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
    }

    private void OnDisable()
    {
        boxes.Remove(this);
        if (boxes.Count == 0) Unregister();
    }

    private void Start()
    {
        foreach (Player3DMovement haru in FindObjectsByType<Player3DMovement>(FindObjectsInactive.Include))
            if (haru.GetComponent<HaruBoxPusher>() == null) haru.gameObject.AddComponent<HaruBoxPusher>();

        // Dudu lives on the wall's plane; a crate pushed against a wall must not shove him.
        Collider[] own = GetComponentsInChildren<Collider>();
        foreach (DuduSurfaceMovement dudu in FindObjectsByType<DuduSurfaceMovement>(FindObjectsInactive.Include))
            foreach (Collider duduCollider in dudu.GetComponentsInChildren<Collider>(true))
                foreach (Collider crate in own)
                    Physics.IgnoreCollision(duduCollider, crate);

        NetworkManager manager = NetworkManager.Singleton;
        if (GameSession.Current == null || manager == null || !manager.IsConnectedClient) return;
        following = !manager.IsHost;
        if (following)
        {
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }
        Register(manager);
    }

    // Called by Haru's controller when he walks into the crate.
    public void Push(Vector3 direction)
    {
        if (following) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        direction.Normalize();
        Vector3 velocity = body.linearVelocity;
        body.linearVelocity = new Vector3(direction.x * pushSpeed, velocity.y, direction.z * pushSpeed);
        lastPushTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (following)
        {
            if (!hasRemotePose) return;
            float blend = 1f - Mathf.Exp(-14f * Time.fixedDeltaTime);
            body.MovePosition(Vector3.Distance(body.position, remotePosition) > 3f
                ? remotePosition : Vector3.Lerp(body.position, remotePosition, blend));
            body.MoveRotation(Quaternion.Slerp(body.rotation, remoteRotation, blend));
            return;
        }
        // Stops where Haru leaves it instead of skating on.
        if (Time.time - lastPushTime > 0.12f)
        {
            Vector3 velocity = body.linearVelocity;
            float damp = Mathf.Exp(-10f * Time.fixedDeltaTime);
            body.linearVelocity = new Vector3(velocity.x * damp, velocity.y, velocity.z * damp);
        }
    }

    private void LateUpdate()
    {
        NetworkManager manager = registeredWith;
        if (following || manager == null || !manager.IsHost || boxes.Count == 0 || boxes[0] != this) return;
        if (Time.unscaledTime < nextSendTime) return;
        nextSendTime = Time.unscaledTime + SendInterval;
        SendAll(manager);
    }

    private static void Register(NetworkManager manager)
    {
        if (registeredWith == manager) return;
        Unregister();
        registeredWith = manager;
        if (!manager.IsHost)
            manager.CustomMessagingManager.RegisterNamedMessageHandler(PoseMessage, OnPoses);
    }

    private static void Unregister()
    {
        if (registeredWith != null && !registeredWith.IsHost && registeredWith.CustomMessagingManager != null)
            registeredWith.CustomMessagingManager.UnregisterNamedMessageHandler(PoseMessage);
        registeredWith = null;
    }

    private static void SendAll(NetworkManager manager)
    {
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;
        using var writer = new FastBufferWriter(8 + boxes.Count * 28, Allocator.Temp);
        writer.WriteValueSafe(sceneIndex);
        writer.WriteValueSafe(boxes.Count);
        foreach (PushableBox box in boxes)
        {
            writer.WriteValueSafe(box.body.position);
            writer.WriteValueSafe(box.body.rotation);
        }
        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(PoseMessage, clientId, writer, NetworkDelivery.UnreliableSequenced);
    }

    private static void OnPoses(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId || !reader.TryBeginRead(8)) return;
        reader.ReadValueSafe(out int sceneIndex);
        if (sceneIndex != SceneManager.GetActiveScene().buildIndex) return;
        reader.ReadValueSafe(out int count);
        if (count != boxes.Count || !reader.TryBeginRead(count * 28)) return;
        foreach (PushableBox box in boxes)
        {
            reader.ReadValueSafe(out box.remotePosition);
            reader.ReadValueSafe(out box.remoteRotation);
            box.hasRemotePose = true;
        }
    }

    private static string PathOf(Transform target) => target.parent == null
        ? target.name : PathOf(target.parent) + "/" + target.name;
}
