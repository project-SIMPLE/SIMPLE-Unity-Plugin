using UnityEngine;

[DisallowMultipleComponent]
public class GamaPreviewObject : MonoBehaviour
{
    public bool previewOnly = true;
    public bool canBeReusedAtRuntime = false;
    public string speciesName = string.Empty;
    public string agentId = string.Empty;
    public string geometryHash = string.Empty;
    public int sourceTick = -1;
}
