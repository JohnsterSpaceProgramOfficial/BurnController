using BepInEx;
using HarmonyLib;
using KSP.UI.Binding;
using SpaceWarp;
using SpaceWarp.API.Assets;
using SpaceWarp.API.Mods;
using SpaceWarp.API.Game;
using SpaceWarp.API.Game.Extensions;
using SpaceWarp.API.UI;
using SpaceWarp.API.UI.Appbar;
using UnityEngine;
using KSP.Sim.impl;

namespace BurnController;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(SpaceWarpPlugin.ModGuid, SpaceWarpPlugin.ModVer)]
public class BurnControllerPlugin : BaseSpaceWarpPlugin
{
    // These are useful in case some other mod wants to add a dependency to this one
    public const string ModGuid = MyPluginInfo.PLUGIN_GUID;
    public const string ModName = MyPluginInfo.PLUGIN_NAME;
    public const string ModVer = MyPluginInfo.PLUGIN_VERSION;
    
    private bool _isWindowOpen;
    private Rect _windowRect;

    private const string ToolbarFlightButtonID = "BTN-BurnControllerFlight";

    public static BurnControllerPlugin Instance { get; set; }

    /// <summary>
    /// Runs when the mod is first initialized.
    /// </summary>
    public override void OnInitialized()
    {
        base.OnInitialized();

        Instance = this;

        // Register Flight AppBar button
        Appbar.RegisterAppButton(
            "Burn Controller",
            ToolbarFlightButtonID,
            AssetManager.GetAsset<Texture2D>($"{SpaceWarpMetadata.ModID}/images/icon.png"),
            isOpen =>
            {
                _isWindowOpen = isOpen;
                GameObject.Find(ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(isOpen);
            }
        );

        // Register all Harmony patches in the project
        Harmony.CreateAndPatchAll(typeof(BurnControllerPlugin).Assembly);

        // Fetch a configuration value or create a default one if it does not exist
        //CFG_DebugMode = Config.Bind("Mod Settings", "Debug Mode", false, "Enable this to show the window on the title screen.");

        // Log the config value into <KSP2 Root>/BepInEx/LogOutput.log
        //Logger.LogInfo($"Debug Mode: {CFG_DebugMode.Value}");
    }

    private void Update()
    {
        if (burnStatus == BurnStatus.Waiting && getTimeBefore)
        {
            if (timeBeforeBurn > 0)
            {
                timeBeforeBurn -= 1f * Time.deltaTime;
            }
            else if (timeBeforeBurn <= 0)
            {
                SetBurnStatus(BurnStatus.InProgress);
                getTimeBefore = false;
            }
        }
        if (burnStatus == BurnStatus.InProgress && getTimeLeft)
        {
            if (timeLeftToBurn > 0)
            {
                if (hasActiveVehicle)
                {
                    activeVessel.SetMainThrottle(thrustPercentageInt / 100f);
                }
                timeLeftToBurn -= 1f * Time.deltaTime;
            }
            else if (timeLeftToBurn <= 0)
            {
                activeVessel.SetMainThrottle(0f);
                SetBurnStatus(BurnStatus.Completed);
                getTimeLeft = false;
            }
        }
        if (burnStatus == BurnStatus.Stopped)
        {
            activeVessel.SetMainThrottle(0f);
            hasActiveVehicle = false;
        }
    }

    /// <summary>
    /// Draws a simple UI window when <code>this._isWindowOpen</code> is set to <code>true</code>.
    /// </summary>
    private void OnGUI()
    {
        // Set the UI
        GUI.skin = Skins.ConsoleSkin;

        if (_isWindowOpen)
        {
            if (burnStatus == BurnStatus.None)
            {
                _windowRect = GUILayout.Window(
                    GUIUtility.GetControlID(FocusType.Passive),
                    _windowRect,
                    FillWindow,
                    "<color=orange>// Burn Controller " + ModVer + "</color>",
                    GUILayout.Width(500),
                    GUILayout.Height(200)
                );
            }
            else
            {
                _windowRect = GUILayout.Window(
                    GUIUtility.GetControlID(FocusType.Passive),
                    _windowRect,
                    FillWindow,
                    "<color=orange>// Burn Controller " + ModVer + "</color>",
                    GUILayout.Width(500),
                    GUILayout.Height(125)
                );
            }
        }
    }

    /// <summary>
    /// Defines the content of the UI window drawn in the <code>OnGui</code> method.
    /// </summary>
    /// <param name="windowID"></param>
    private void FillWindow(int windowID)
    {
        GUILayout.BeginVertical();
        if (GUI.Button(new Rect(_windowRect.width - 35, 5, 30, 30), "<color=red><size=30>X</size></color>"))
        {
            if (_isWindowOpen)
            {
                _isWindowOpen = false;
                GameObject.Find(ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(false);
            }
        }
        GUILayout.EndVertical();

        if (burnStatus == BurnStatus.None)
        {
            GUILayout.Label("<size=20><i>BURN SETTINGS</i></size>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Start Burn In...");

            hoursToBurn = GUILayout.TextField(hoursToBurn, 2);
            if (float.TryParse(hoursToBurn, out hoursToBurnFloat))
            {
                if (Mathf.RoundToInt(hoursToBurnFloat) > 23)
                {
                    hoursToBurn = GUILayout.TextField("23", 2);
                }
                else if (Mathf.RoundToInt(hoursToBurnFloat) < 0)
                {
                    hoursToBurn = GUILayout.TextField("0", 2);
                }
            }
            if (hoursToBurn.Contains("."))
            {
                hoursToBurn = GUILayout.TextField(hoursToBurn.Remove(1), 2);
            }
            if (hoursToBurnFloat == 1)
            {
                GUILayout.Label("Hour");
            }
            else
            {
                GUILayout.Label("Hours");
            }

            minsToBurn = GUILayout.TextField(minsToBurn, 2);
            if (float.TryParse(minsToBurn, out minsToBurnFloat))
            {
                if (Mathf.RoundToInt(minsToBurnFloat) > 59)
                {
                    minsToBurn = GUILayout.TextField("59", 2);
                }
                else if (Mathf.RoundToInt(minsToBurnFloat) < 0)
                {
                    minsToBurn = GUILayout.TextField("0", 2);
                }
            }
            if (minsToBurn.Contains("."))
            {
                minsToBurn = GUILayout.TextField(minsToBurn.Remove(1), 2);
            }
            if (minsToBurnFloat == 1)
            {
                GUILayout.Label("Minute");
            }
            else
            {
                GUILayout.Label("Minutes");
            }

            secsToBurn = GUILayout.TextField(secsToBurn, 2);
            if (float.TryParse(secsToBurn, out secsToBurnFloat))
            {
                if (Mathf.RoundToInt(secsToBurnFloat) > 59)
                {
                    secsToBurn = GUILayout.TextField("59", 2);
                }
                else if (Mathf.RoundToInt(secsToBurnFloat) < 0)
                {
                    secsToBurn = GUILayout.TextField("0", 2);
                }
            }
            if (secsToBurn.Contains("."))
            {
                secsToBurn = GUILayout.TextField(secsToBurn.Remove(1), 2);
            }
            if (secsToBurnFloat == 1)
            {
                GUILayout.Label("Second");
            }
            else
            {
                GUILayout.Label("Seconds");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("At...");
            thrustPercentage = GUILayout.TextField(thrustPercentage, 3);
            if (int.TryParse(thrustPercentage, out thrustPercentageInt))
            {
                if (thrustPercentageInt > 100)
                {
                    thrustPercentage = GUILayout.TextField("100", 3);
                }
                else if (thrustPercentageInt < 1)
                {
                    thrustPercentage = GUILayout.TextField("1", 3);
                }
            }
            if (thrustPercentage.Contains("."))
            {
                thrustPercentage = GUILayout.TextField(thrustPercentage.Remove(1), 3);
            }
            GUILayout.Label("% Throttle");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("For A Total Duration Of...");

            burnLengthHours = GUILayout.TextField(burnLengthHours, 2);
            if (float.TryParse(burnLengthHours, out burnLengthHoursFloat))
            {
                if (Mathf.RoundToInt(burnLengthHoursFloat) > 23)
                {
                    burnLengthHours = GUILayout.TextField("23", 2);
                }
                else if (Mathf.RoundToInt(burnLengthHoursFloat) < 0)
                {
                    burnLengthHours = GUILayout.TextField("0", 2);
                }
            }
            if (burnLengthHours.Contains("."))
            {
                burnLengthHours = GUILayout.TextField(burnLengthHours.Remove(1), 2);
            }
            if (burnLengthHoursFloat == 1)
            {
                GUILayout.Label("Hour");
            }
            else
            {
                GUILayout.Label("Hours");
            }

            burnLengthMins = GUILayout.TextField(burnLengthMins, 2);
            if (float.TryParse(burnLengthMins, out burnLengthMinsFloat))
            {
                if (Mathf.RoundToInt(burnLengthMinsFloat) > 59)
                {
                    burnLengthMins = GUILayout.TextField("59", 2);
                }
                else if (Mathf.RoundToInt(burnLengthMinsFloat) < 0)
                {
                    burnLengthMins = GUILayout.TextField("0", 2);
                }
            }
            if (burnLengthMins.Contains("."))
            {
                burnLengthMins = GUILayout.TextField(burnLengthMins.Remove(1), 2);
            }
            if (burnLengthMinsFloat == 1)
            {
                GUILayout.Label("Minute");
            }
            else
            {
                GUILayout.Label("Minutes");
            }

            burnLengthSecs = GUILayout.TextField(burnLengthSecs, 2);
            if (float.TryParse(burnLengthSecs, out burnLengthSecsFloat))
            {
                if (Mathf.RoundToInt(burnLengthSecsFloat) > 59)
                {
                    burnLengthSecs = GUILayout.TextField("59", 2);
                }
                else if (Mathf.RoundToInt(burnLengthSecsFloat) < 0)
                {
                    burnLengthSecs = GUILayout.TextField("0", 2);
                }
            }
            if (secsToBurn.Contains("."))
            {
                burnLengthSecs = GUILayout.TextField(burnLengthSecs.Remove(1), 2);
            }
            if (burnLengthSecsFloat == 1)
            {
                GUILayout.Label("Second");
            }
            else
            {
                GUILayout.Label("Seconds");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if ((secsToBurnFloat > 0) || (minsToBurnFloat > 0) || (hoursToBurnFloat > 0))
            {
                if ((burnLengthSecsFloat > 0) || (burnLengthMinsFloat > 0) || (burnLengthHoursFloat > 0))
                {
                    if (GUILayout.Button("<size=30>SETUP BURN</size>", GUILayout.Height(40)))
                    {
                        SetBurnStatus(BurnStatus.Waiting);
                    }
                }
            }
        }
        else
        {
            if (timeLeftToBurn <= 0 && burnStatus == BurnStatus.Completed)
            {
                GUILayout.Label("<color=#00FF00><size=30>Burn Complete!</size></color>");
                if (GUILayout.Button("<size=30>RETURN</size>", GUILayout.Height(40)))
                {
                    getTimeLeft = false;
                    getTimeBefore = false;
                    if (startEngines)
                    {
                        activeVessel = Vehicle.ActiveVesselVehicle;
                        if (activeVessel != null)
                        {
                            hasActiveVehicle = false;
                        }
                        startEngines = false;
                    }
                    SetBurnStatus(BurnStatus.None);
                }
            }

            if (burnStatus == BurnStatus.Waiting)
            {
                if (!getTimeBefore)
                {
                    float hoursToSeconds = hoursToBurnFloat * 60 * 60;
                    float minutesToSeconds = minsToBurnFloat * 60;
                    timeBeforeBurn = hoursToSeconds + minutesToSeconds + secsToBurnFloat;
                    getTimeBefore = true;
                }
                if (timeBeforeBurn > 30)
                {
                    GUILayout.Label("<color=#00FF00><size=25>Starting Burn In " + string.Format("{0:00}:{1:00}:{2:00}", Mathf.FloorToInt(timeBeforeBurn / 3600) % 24, Mathf.FloorToInt(timeBeforeBurn / 60) % 60, Mathf.FloorToInt(timeBeforeBurn % 60)) + "</size></color>");
                }
                else if (timeBeforeBurn > 10 && timeBeforeBurn < 30)
                {
                    GUILayout.Label("<color=#FF8000><size=25>Starting Burn In " + string.Format("{0:00}:{1:00}:{2:00}", Mathf.FloorToInt(timeBeforeBurn / 3600) % 24, Mathf.FloorToInt(timeBeforeBurn / 60) % 60, Mathf.FloorToInt(timeBeforeBurn % 60)) + "</size></color>");
                }
                else if (timeBeforeBurn < 10 && timeBeforeBurn > 0)
                {
                    GUILayout.Label("<color=#FF0000><size=25>Starting Burn In " + string.Format("{0:00}:{1:00}:{2:00}", Mathf.FloorToInt(timeBeforeBurn / 3600) % 24, Mathf.FloorToInt(timeBeforeBurn / 60) % 60, Mathf.FloorToInt(timeBeforeBurn % 60)) + "</size></color>");
                }

                if (GUILayout.Button("<size=30>CANCEL BURN</size>", GUILayout.Height(40)))
                {
                    getTimeBefore = false;
                    SetBurnStatus(BurnStatus.None);
                }
            }
            if (burnStatus == BurnStatus.InProgress)
            {
                if (!startEngines)
                {
                    activeVessel = Vehicle.ActiveVesselVehicle;
                    if (activeVessel != null)
                    {
                        hasActiveVehicle = true;
                    }
                    startEngines = true;
                }
                if (!getTimeLeft)
                {
                    float hoursToSeconds = burnLengthHoursFloat * 60 * 60;
                    float minutesToSeconds = burnLengthMinsFloat * 60;
                    timeLeftToBurn = hoursToSeconds + minutesToSeconds + burnLengthSecsFloat;
                    getTimeLeft = true;
                }

                if (timeLeftToBurn > 30)
                {
                    GUILayout.Label("<color=#00FF00><size=25>Completing Burn In " + string.Format("{0:00}:{1:00}:{2:00}", Mathf.FloorToInt(timeLeftToBurn / 3600) % 24, Mathf.FloorToInt(timeLeftToBurn / 60) % 60, Mathf.FloorToInt(timeLeftToBurn % 60)) + "</size></color>");
                }
                else if (timeLeftToBurn > 10 && timeLeftToBurn < 30)
                {
                    GUILayout.Label("<color=#FF8000><size=25>Completing Burn In " + string.Format("{0:00}:{1:00}:{2:00}", Mathf.FloorToInt(timeLeftToBurn / 3600) % 24, Mathf.FloorToInt(timeLeftToBurn / 60) % 60, Mathf.FloorToInt(timeLeftToBurn % 60)) + "</size></color>");
                }
                else if (timeLeftToBurn < 10 && timeLeftToBurn > 0)
                {
                    GUILayout.Label("<color=#FF0000><size=25>Completing Burn In " + string.Format("{0:00}:{1:00}:{2:00}", Mathf.FloorToInt(timeLeftToBurn / 3600) % 24, Mathf.FloorToInt(timeLeftToBurn / 60) % 60, Mathf.FloorToInt(timeLeftToBurn % 60)) + "</size></color>");
                }

                if (GUILayout.Button("<size=30>STOP BURN</size>", GUILayout.Height(40)))
                {
                    SetBurnStatus(BurnStatus.Stopped);
                }
            }
            if (burnStatus == BurnStatus.Stopped)
            {
                GUILayout.Label("<color=#FF0000><size=30>Burn Stopped...</size></color>");

                if (GUILayout.Button("<size=30>RETURN</size>", GUILayout.Height(40)))
                {
                    getTimeLeft = false;
                    getTimeBefore = false;
                    if (startEngines)
                    {
                        hasActiveVehicle = false;
                        startEngines = false;
                    }
                    SetBurnStatus(BurnStatus.None);
                }
            }
        }

        GUI.DragWindow(new Rect(0, 0, 10000, 40));
    }

    private void SetBurnStatus(BurnStatus newStatus)
    {
        burnStatus = newStatus;
    }

    private enum BurnStatus
    {
        None,
        Waiting,
        InProgress,
        Completed,
        Stopped
    }

    private string secsToBurn = "0";
    private string minsToBurn = "0";
    private string hoursToBurn = "0";

    private float secsToBurnFloat;
    private float minsToBurnFloat;
    private float hoursToBurnFloat;

    private string thrustPercentage = "100";
    private int thrustPercentageInt = 100;

    private string burnLengthSecs = "0";
    private string burnLengthMins = "0";
    private string burnLengthHours = "0";

    private float burnLengthSecsFloat;
    private float burnLengthMinsFloat;
    private float burnLengthHoursFloat;

    private float timeBeforeBurn;
    private float timeLeftToBurn;

    private bool getTimeBefore;
    private bool getTimeLeft;
    private bool startEngines;

    private BurnStatus burnStatus;
    private VesselVehicle activeVessel;
    private bool hasActiveVehicle = false;
}
