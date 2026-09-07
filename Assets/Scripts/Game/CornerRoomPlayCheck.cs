#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Runs real fixed-step physics in Play Mode and restores the saved scene on exit.
[DefaultExecutionOrder(-10000)]
public sealed class CornerRoomPlayCheck : MonoBehaviour
{
    private CornerRoomLevel level;
    private DuduSurfaceMovement player;
    private int direction;
    private readonly HashSet<DuduSurface> visited = new HashSet<DuduSurface>();
    private static readonly FieldInfo Input = typeof(DuduSurfaceMovement).GetField("horizontalInput",BindingFlags.Instance|BindingFlags.NonPublic);
    private readonly List<string> results = new List<string>();
    private bool failed;
    private void FixedUpdate()
    {
        if (player == null) return;
        Input.SetValue(player,(float)direction);
        visited.Add(player.CurrentSurface);
    }
    private void Check(bool condition,string message)
    {
        results.Add((condition ? "PASS " : "FAIL ") + message);
        failed |= !condition;
    }
    private void Place(int index,float x)
    {
        var surface = level.surfaces[index];
        player.SetSurface(surface);
        var body = player.GetComponent<Rigidbody>();
        body.position = surface.SurfaceToWorld(new Vector2(x,-1.5f));
        body.rotation = surface.transform.rotation;
        body.linearVelocity = Vector3.zero;
        // SetSurface measures depth, so call again after placement.
        player.SetSurface(surface);
        Physics.SyncTransforms();
    }
    private IEnumerator Start()
    {
        level = FindAnyObjectByType<CornerRoomLevel>();
        player = level.player;
        yield return null;
        Check(level.surfaces.Length == 5 && level.coins.Length == 3,"5 surfaces / 3 coins");
        Check(!level.IsOpen && level.door.activeSelf,"door initially locked");
        Place(4,5.5f);
        direction = 1;
        yield return new WaitForSeconds(2f);
        Check(level.surfaces[4].WorldToSurface(player.transform.position).x < 6.5f && !level.IsClear,"closed door physically blocks player / no early clear");
        direction = 0;
        Place(0,-3.8f);
        visited.Clear();
        direction = 1;
        float deadline = Time.time + 65f;
        while (!level.IsOpen && Time.time < deadline) yield return new WaitForFixedUpdate();
        direction = 0;
        results.Add("INFO forward stopped on surface " +
            (System.Array.IndexOf(level.surfaces, player.CurrentSurface) + 1) +
            " at " + player.CurrentSurface.WorldToSurface(player.transform.position) +
            "; visited=" + visited.Count + "; coins=" + level.CollectedCount);
        Check(visited.Count == 5,"forward traversal visits all 5 surfaces");
        Check(level.CollectedCount == 3,"3 coins disabled by real physics contacts");
        Check(level.IsOpen && !level.door.activeSelf,"all coins open door");
        direction = 1;
        yield return new WaitForSeconds(2f);
        direction = 0;
        Check(level.surfaces[4].WorldToSurface(player.transform.position).x <= 5.81f,
            "2D player stays before the 3D doorway after it opens");
        Check(!level.IsClear,"2D coin collection does not clear the level");
        level.TryCompleteThreeDimensionalGoal(level.player3D);
        Check(level.IsClear,"only the 3D goal completes the level");
        visited.Clear();
        direction = -1;
        deadline = Time.time + 65f;
        while (!(player.CurrentSurface == level.surfaces[0] && level.surfaces[0].WorldToSurface(player.transform.position).x < -3f) && Time.time < deadline)
            yield return new WaitForFixedUpdate();
        direction = 0;
        results.Add("INFO reverse stopped on surface " +
            (System.Array.IndexOf(level.surfaces, player.CurrentSurface) + 1) +
            " at " + player.CurrentSurface.WorldToSurface(player.transform.position) +
            "; visited=" + visited.Count);
        Check(visited.Count == 5 && player.CurrentSurface == level.surfaces[0],"reverse traversal across all four corners");
        Check(level.IsOpen && level.IsClear,"open / clear states stay latched");
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string logDirectory = Path.Combine(projectRoot, "Logs");
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(Path.Combine(logDirectory, "CornerRoomPlayCheck.txt"), results);
        if (failed)
            Debug.LogError(string.Join("\n",results));
        else
            Debug.Log(string.Join("\n",results));
        if (SessionState.GetBool("CornerRoomBatchCheck", false))
        {
            SessionState.SetBool("CornerRoomBatchCheck", false);
            EditorApplication.Exit(failed ? 1 : 0);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
    [MenuItem("Tools/Draw And Go/Verify Corner Room Play Mode")]
    public static void Run()
    {
        SessionState.SetBool("CornerRoomPlayCheck",true);
        EditorApplication.isPlaying = true;
    }
    public static void RunBatch()
    {
        SessionState.SetBool("CornerRoomBatchCheck", true);
        Run();
    }
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("CornerRoomPlayCheck",false))
            {
                SessionState.SetBool("CornerRoomPlayCheck",false);
                new GameObject("Temporary Corner Room Play Check").AddComponent<CornerRoomPlayCheck>();
            }
        };
    }
}
#endif

