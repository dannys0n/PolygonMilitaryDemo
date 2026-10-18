using UnityEngine;
using TMPro;

// Local lobby preview. Membership and readiness will come from a session backend later.
public class ArenaLobbyMenu : MonoBehaviour
{
    public ArenaApplicationMenu applicationMenu;
    public TMP_Text status;
    public const string SandboxScenePath = "Assets/Game/Scenes/Gameplay/MilitaryBase/MilitaryBase_Sandbox.unity";

    public void StartMatch() => applicationMenu.LoadScene(SandboxScenePath);
    public void CreateClass() => ArenaClassEditor.Open();
    public void Callsign() => status.text = "CALLSIGN & KILLSTREAKS\nPlayer customization is not connected yet.";
    public void Barracks() => status.text = "BARRACKS\nCareer statistics are not connected yet.";
    public void Invite() => status.text = "INVITE\nOnline invitations are not connected yet.";
    public void VoteMap() => status.text = "PROTOTYPE ARENA\nMap voting is not connected yet.";
}
