#if UNITY_EDITOR
// Lets the editor show the course this script builds in play (Editor/TutorialCoursePreview),
// by running the same code. That tool undoes the changes this makes to the scene.
public sealed partial class TutorialSequenceController
{
    public void BuildCourseForPreview()
    {
        Awake();
        BuildCourse();
    }
}
#endif
