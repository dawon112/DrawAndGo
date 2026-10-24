using UnityEngine;

// Marks room decor that belongs to Haru's 3D room only (windows cut into a wall,
// ceiling lamps). Dudu's 2D view skips everything under it, so none of it lands on
// Dudu's paper. Built by the Tools > Draw And Go menu (WallWindowTool).
[DisallowMultipleComponent]
public sealed class RoomOnlyDecor : MonoBehaviour
{
    [Tooltip("A window the sun shines through: StageLook turns sun shadows on only in stages that have one.")]
    public bool letsSunIn;
}
