using System.Collections;
using UnityEngine;

// Additional 2D course pieces only; the authored corridor, wall and door transforms stay untouched.
public sealed partial class TutorialSequenceController
{
    [Header("Late tutorial scene references")]
    [SerializeField] private TutorialRoomLevel tutorialRoom;
    [SerializeField] private DuduSurface lastSurface;
    [SerializeField] private GameObject secondRouteLine;
    [SerializeField] private GameObject finalTestRouteLine;
    [SerializeField] private TutorialStarCollectible starPrefab;
    [SerializeField] private DuduMovingObstacle knockbackPrefab;
    [SerializeField] private DuduStainObstacle slowPrefab;
    [SerializeField] private WindZone windPrefab;

    private readonly TutorialStarCollectible[] courseStars = new TutorialStarCollectible[3];
    private DuduSurface[] lateSections;
    private int obstacleProgress;
    private int lateDialoguePhase;
    private Coroutine doorHintRoutine;
    private Material obstacleMaterial;
    private DuduSurface authoredLastSurface;

    private void Awake()
    {
        if (tutorialRoom == null) tutorialRoom = FindAnyObjectByType<TutorialRoomLevel>();
        if (tutorialRoom == null || lastSurface == null || tutorialRoom.Sections == null ||
            tutorialRoom.Sections.Length == 0) return;

        // Wall8 is physically much longer than its old 8-unit logical surface.
        // Match the 2D route/camera span to the authored wall before camera Start.
        DuduSurface approachWall = tutorialRoom.Sections[tutorialRoom.Sections.Length - 2];
        BoxCollider approachCollider = approachWall != null ? approachWall.GetComponent<BoxCollider>() : null;
        if (approachCollider != null)
            approachWall.ConfigureDimensions(
                Mathf.Abs(approachCollider.size.x * approachWall.transform.lossyScale.x), approachWall.Height);

        // Wall11's authored transform faces outward. A small logical surface on its
        // corridor side keeps all 3D wall geometry intact and points Dudu toward the door.
        authoredLastSurface = lastSurface;
        GameObject facing = new GameObject("Tutorial Door-Side 2D Surface", typeof(DuduSurface));
        facing.transform.SetParent(transform, false);
        facing.transform.SetPositionAndRotation(authoredLastSurface.transform.position - authoredLastSurface.Normal * 0.05f,
            Quaternion.AngleAxis(180f, authoredLastSurface.Up) * authoredLastSurface.transform.rotation);
        lastSurface = facing.GetComponent<DuduSurface>();
        lastSurface.ConfigureDimensions(authoredLastSurface.Width, authoredLastSurface.Height);
        tutorialRoom.Sections[tutorialRoom.Sections.Length - 1] = lastSurface;
    }

    private void BuildLateCourse(Transform root, Transform triggers)
    {
        if (tutorialRoom == null) tutorialRoom = FindAnyObjectByType<TutorialRoomLevel>();
        if (tutorialRoom == null || tutorialRoom.Sections == null || tutorialRoom.Sections.Length < 7 ||
            lastSurface == null || starPrefab == null || knockbackPrefab == null || slowPrefab == null || windPrefab == null)
        {
            Debug.LogError("Tutorial: late-course scene references are incomplete.", this);
            return;
        }
        lateSections = tutorialRoom.Sections;
        DuduSurface second = lateSections[1], third = lateSections[2], corner = lateSections[3],
            fourth = lateSections[4], fifth = lateSections[5];
        third.nextSurface = corner;
        corner.previousSurface = third;
        corner.nextSurface = fourth;
        fourth.previousSurface = corner;
        fifth.nextSurface = lastSurface;
        lastSurface.previousSurface = fifth;
        lastSurface.nextSurface = null;

        // Only the authored 2D route lines are substituted during play.
        if (secondRouteLine != null) secondRouteLine.SetActive(false);
        if (finalTestRouteLine != null) finalTestRouteLine.SetActive(false);
        BuildLateGround(root, second, "ObstacleGround", -second.Width * 0.5f, second.Width * 0.5f);
        BuildLateGround(root, third, "ObstacleTurnGround", -third.Width * 0.5f, third.Width * 0.5f);
        BuildLateGround(root, corner, "StarApproachGround", -corner.Width * 0.5f, corner.Width * 0.5f);
        BuildLateGround(root, fifth, "FinalGapLeft", -fifth.Width * 0.5f, -2.3f);
        BuildLateGround(root, fifth, "FinalGapRight", 2.3f, fifth.Width * 0.5f);
        BuildLateGround(root, lastSurface, "DoorApproachGround", -lastSurface.Width * 0.5f, lastSurface.Width * 0.5f);

        obstacleMaterial = new Material(Shader.Find("Sprites/Default"));
        obstacleMaterial.color = new Color(0.22f, 0.31f, 0.39f);
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Tutorial_Block";
        block.transform.SetParent(root, false);
        block.transform.SetPositionAndRotation(second.SurfaceToWorld(new Vector2(-1.8f, groundSurfaceY + 0.43f)) + second.Normal * 0.09f, second.transform.rotation);
        block.transform.localScale = new Vector3(0.75f, 0.86f, 0.25f);
        // Dudu is constrained to a thin 2D plane. A deeper invisible collider makes
        // the block resolve contact sideways instead of pushing Dudu off that plane.
        block.GetComponent<BoxCollider>().size = new Vector3(1f, 1f, 3.2f);
        block.GetComponent<Renderer>().sharedMaterial = obstacleMaterial;

        DuduMovingObstacle knockback = Instantiate(knockbackPrefab, root);
        knockback.name = "Tutorial_Block_Knockback";
        BoxCollider duduBody = dudu.GetComponent<BoxCollider>();
        float duduHeight = duduBody != null ? duduBody.bounds.size.y : 1.3f;
        knockback.Configure(second, DuduMovingObstacle.MovementAxis.Vertical,
            new Vector2(1.15f, groundSurfaceY + 1.35f + duduHeight), 0.7f, 3.8f);
        knockback.SetSurfaceNormalOffset(0.09f);
        BoxCollider knockbackCollider = knockback.GetComponent<BoxCollider>();
        if (knockbackCollider != null)
        {
            Vector3 size = knockbackCollider.size;
            size.z = Mathf.Max(size.z, 4.7f);
            knockbackCollider.size = size;
        }

        DuduStainObstacle slow = Instantiate(slowPrefab, root);
        slow.name = "Tutorial_Fog_Slow";
        slow.Configure(third, new Vector2(-2.5f, groundSurfaceY + 0.08f), DuduStainObstacle.EffectType.Slow);
        slow.SetSurfaceNormalOffset(0.09f);
        slow.SetEffectDuration(0.15f);
        TightenFogTrigger(slow);

        WindZone wind = Instantiate(windPrefab, root);
        wind.name = "Tutorial_Zone_Windy";
        wind.transform.SetPositionAndRotation(third.SurfaceToWorld(new Vector2(2f, groundSurfaceY + 0.9f)) + third.Normal * 0.08f, third.transform.rotation);
        wind.transform.localScale = new Vector3(0.18f, 0.25f, 0.03f);
        wind.GetComponent<BoxCollider>().size = new Vector3(14f, 7f, 2f);
        wind.Configure(third, Vector2.left, 1.3f);

        courseStars[0] = SpawnStar(root, fourth, -2.6f, "Tutorial_Star_1");
        courseStars[1] = SpawnStar(root, fifth, 2.95f, "Tutorial_Star_2");
        // A familiar obstacle is revisited before the final star; the corridor after it stays clear.
        DuduStainObstacle review = Instantiate(slowPrefab, root);
        review.name = "FinalTest_Fog_Slow";
        review.Configure(lastSurface, new Vector2(-3.8f, groundSurfaceY + 0.08f), DuduStainObstacle.EffectType.Slow);
        review.SetSurfaceNormalOffset(0.09f);
        review.SetEffectDuration(0.15f);
        TightenFogTrigger(review);
        courseStars[2] = SpawnStar(root, lastSurface, 7f, "Tutorial_Star_3");
        tutorialRoom.Configure(lateSections, courseStars, tutorialRoom.Door, dudu);

        GameObject exit = new GameObject("TutorialExitTrigger", typeof(BoxCollider), typeof(TutorialDoorExitTrigger));
        exit.transform.SetParent(triggers, false);
        if (tutorialRoom.Door != null)
            exit.transform.SetPositionAndRotation(tutorialRoom.Door.transform.position + tutorialRoom.Door.transform.forward * 0.4f,
                tutorialRoom.Door.transform.rotation);
        BoxCollider exitCollider = exit.GetComponent<BoxCollider>();
        exitCollider.isTrigger = true;
        exitCollider.size = new Vector3(2f, 3f, 1.8f);
        exit.GetComponent<TutorialDoorExitTrigger>().Configure(this);
    }

    private void BuildLateGround(Transform root, DuduSurface target, string name, float left, float right)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = name;
        ground.transform.SetParent(root, false);
        ground.transform.SetPositionAndRotation(target.SurfaceToWorld(new Vector2((left + right) * 0.5f, groundSurfaceY - 0.07f)) + target.Normal * 0.055f,
            target.transform.rotation);
        ground.transform.localScale = new Vector3(right - left, 0.14f, 0.18f);
        ground.GetComponent<Renderer>().sharedMaterial = originalStartLine.GetComponent<Renderer>().sharedMaterial;
    }

    private TutorialStarCollectible SpawnStar(Transform root, DuduSurface target, float x, string name)
    {
        TutorialStarCollectible star = Instantiate(starPrefab, root);
        star.name = name;
        star.transform.SetPositionAndRotation(target.SurfaceToWorld(new Vector2(x, groundSurfaceY + 0.8f)) + target.Normal * 0.13f,
            target.transform.rotation);
        return star;
    }

    private static void TightenFogTrigger(DuduStainObstacle stain)
    {
        SphereCollider trigger = stain.GetComponent<SphereCollider>();
        if (trigger == null) return;
        trigger.center = Vector3.zero;
        trigger.radius = 2.2f; // ~0.73 world units at the prefab's 0.33 scale.
    }

    private void InitializeLateTutorial()
    {
        if (tutorialRoom != null) tutorialRoom.StarCollected += OnTutorialStarCollected;
    }

    private void BeginObstacleTutorial()
    {
        CurrentStep = TutorialStep.Tutorial_04_Obstacle;
        obstacleProgress = 0;
        SetLateInstruction("장애물을 살펴보며 앞으로 가보세요!");
    }

    private void UpdateLateTutorial()
    {
        if (lateSections == null || dudu.CurrentSurface == null) return;
        if (CurrentStep == TutorialStep.Tutorial_04_Obstacle)
        {
            DuduSurface here = dudu.CurrentSurface;
            float x = here.WorldToSurface(dudu.transform.position).x;
            int progress = here == lateSections[1] ? (x > 2f ? 2 : x > -0.9f ? 1 : 0) :
                here == lateSections[2] ? (x > 3.3f ? 4 : x > -1f ? 3 : 2) :
                here == lateSections[3] || here == lateSections[4] || here == lateSections[5] || here == lastSurface ? 4 : 0;
            if (progress > obstacleProgress)
            {
                obstacleProgress = progress;
                string[] hints = { "block를 넘어가세요!", "block_knockback을 피해보세요!", "fog_slow를 통과해보세요!", "zone_windy를 통과해보세요!" };
                if (progress < 4) SetLateInstruction(hints[progress]);
                else instructionPanel.SetActive(false);
            }
            if (obstacleProgress == 4 && here == lateSections[4] && x >= -4.8f && !dialoguePanel.activeSelf)
            {
                CurrentStep = TutorialStep.Tutorial_05_Star;
                lateDialoguePhase = 1;
                ShowDialogue(new[] { "두두", "하루" }, new[] { "하루! 저기 반짝이는 게 있어!", "한번 가져가 볼래?" });
            }
        }
    }

    public bool CanCollectStar(TutorialStarCollectible star)
    {
        if (courseStars[0] == star) return CurrentStep == TutorialStep.Tutorial_05_Star &&
            lateDialoguePhase == 2 && dudu.CurrentSurface == lateSections[4];
        if (courseStars[1] == star) return CurrentStep == TutorialStep.Tutorial_06_FinalTest &&
            tutorialRoom.CollectedStars == 1 && dudu.CurrentSurface == lateSections[5];
        if (courseStars[2] == star) return CurrentStep == TutorialStep.Tutorial_06_FinalTest &&
            tutorialRoom.CollectedStars == 2 && dudu.CurrentSurface == lastSurface;
        return false;
    }

    private void OnTutorialStarCollected(int count)
    {
        tutorialRoom.ShowStarCounter(true);
        tutorialRoom.PulseStarCounter();
        if (count == 1 && CurrentStep == TutorialStep.Tutorial_05_Star)
        {
            lateDialoguePhase = 3;
            ShowDialogue(new[] { "두두", "하루" }, new[] { "별을 모으니까 뭔가 반응했어!", "조금 더 찾아보자." });
        }
        else if (count == 3 && CurrentStep == TutorialStep.Tutorial_06_FinalTest)
        {
            CurrentStep = TutorialStep.Tutorial_07_Door;
            lateDialoguePhase = 4;
            ShowDialogue(new[] { "두두", "하루", "두두" }, new[] {
                "하루! 문이 열렸어!", "별을 모으면 다음으로 갈 수 있나 봐.", "그럼 가보자!" });
        }
    }

    private void FinishLateDialogue()
    {
        if (CurrentStep == TutorialStep.Tutorial_05_Star && lateDialoguePhase == 1)
        {
            lateDialoguePhase = 2;
            SetLateInstruction("별을 획득해보세요! ★");
        }
        else if (CurrentStep == TutorialStep.Tutorial_05_Star && lateDialoguePhase == 3 && tutorialRoom.CollectedStars == 1)
        {
            CurrentStep = TutorialStep.Tutorial_06_FinalTest;
            instructionPanel.SetActive(false);
        }
        else if (CurrentStep == TutorialStep.Tutorial_07_Door && lateDialoguePhase == 4)
        {
            SetLateInstruction("필요한 별을 모으면 문이 열립니다!");
            if (doorHintRoutine != null) StopCoroutine(doorHintRoutine);
            doorHintRoutine = StartCoroutine(HideDoorHint());
        }
    }

    private IEnumerator HideDoorHint()
    {
        yield return new WaitForSeconds(2.5f);
        if (CurrentStep == TutorialStep.Tutorial_07_Door) instructionPanel.SetActive(false);
    }

    private void SetLateInstruction(string text)
    {
        instruction.text = text;
        instructionPanel.SetActive(true);
    }

    public void TryCompleteAtDoor(Player3DMovement player)
    {
        if (player != haru || CurrentStep != TutorialStep.Tutorial_07_Door ||
            tutorialRoom == null || !tutorialRoom.DoorOpened || tutorialRoom.CollectedStars != 3) return;
        CurrentStep = TutorialStep.Tutorial_Clear;
        instructionPanel.SetActive(false);
        dialoguePanel.SetActive(false);
        GameViewManager view = FindAnyObjectByType<GameViewManager>();
        if (view != null) view.PlayAllStarsCollectedSequence(tutorialRoom);
    }

    private void DisposeLateTutorial()
    {
        if (tutorialRoom != null) tutorialRoom.StarCollected -= OnTutorialStarCollected;
        if (obstacleMaterial != null) Destroy(obstacleMaterial);
        if (secondRouteLine != null) secondRouteLine.SetActive(true);
        if (finalTestRouteLine != null) finalTestRouteLine.SetActive(true);
        if (tutorialRoom != null && authoredLastSurface != null && tutorialRoom.Sections != null &&
            tutorialRoom.Sections.Length > 0)
            tutorialRoom.Sections[tutorialRoom.Sections.Length - 1] = authoredLastSurface;
    }
}
