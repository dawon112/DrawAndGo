using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The tutorial's speech box (Resources/UI/TutorialDialogue.prefab): a paper strip with
// the speaker's portrait on the left. Haru speaks in orange with his portrait,
// Dudu in blue with his crayon drawing. Layout and colours are edited on the prefab.
[DisallowMultipleComponent]
public sealed class TutorialDialogueBox : MonoBehaviour
{
    public const string DuduName = "두두";

    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text lineText;
    [SerializeField] private TMP_Text continueHint;
    [SerializeField] private Image haruPortrait;
    [SerializeField] private Image duduPortrait;
    [SerializeField] private Color haruColor = new Color(0.8f, 0.47f, 0.24f);
    [SerializeField] private Color duduColor = new Color(0.18f, 0.53f, 0.82f);

    public string Speaker => nameText.text;
    public string Line => lineText.text;

    public void Show(string speaker, string line)
    {
        bool dudu = speaker == DuduName;
        nameText.text = speaker;
        lineText.text = line;
        Color color = dudu ? duduColor : haruColor;
        nameText.color = color;
        lineText.color = color;
        if (continueHint != null) continueHint.color = new Color(color.r, color.g, color.b, 0.6f);
        haruPortrait.gameObject.SetActive(!dudu);
        duduPortrait.gameObject.SetActive(dudu);
    }
}
