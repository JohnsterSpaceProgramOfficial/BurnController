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
using KSP.Sim.Maneuver;
using KSP.Sim;
using KSP.Game;
using System.Collections;
using KSP.Sim.DeltaV;

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

        //Initialize the default burn type as ManualBurn
        SetBurnType(BurnType.ManualBurn);

        // Fetch a configuration value or create a default one if it does not exist
        //CFG_DebugMode = Config.Bind("Mod Settings", "Debug Mode", false, "Enable this to show the window on the title screen.");

        // Log the config value into <KSP2 Root>/BepInEx/LogOutput.log
        //Logger.LogInfo($"Debug Mode: {CFG_DebugMode.Value}");
        isPrerelease = true;
        prereleaseName = "PRERELEASE";
        isDebug = false;
    }

    private void Update()
    {
        if (burnType == BurnType.ManualBurn)
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
                    //Code for a possible 0.9 prerelease update
                    if (!freezeTimeLeft)
                    {
                        currentDeltaV = GetCurrentDeltaV();
                        if (currentDeltaV <= 0)
                        {
                            StartCoroutine(StageUntilEngineFound());
                        }
                    }
                    //Code for a possible 0.9 prerelease update

                    if (constantThrottle) //If the use constant throttle toggle is checked, keep the thrust constant
                    {
                        if (hasActiveVehicle)
                        {
                            //Code for a possible 0.9 prerelease update
                            if (!freezeTimeLeft)
                            {
                                activeVessel.SetMainThrottle(thrustPercentageInt / 100f);
                            }
                        }
                    }
                    else //If the use constant throttle toggle is unchecked, change the thrust over time
                    {
                        if (!startedThrustChanger)
                        {
                            if (hasActiveVehicle)
                            {
                                activeVessel.SetMainThrottle(currentThrust);
                            }
                            StartCoroutine(ChangeThrustOverTime(startThrust / 100f, endThrust / 100f, timeLeftToBurn));
                        }
                    }

                    //Code for a possible 0.9 prerelease update
                    if (!freezeTimeLeft)
                    {
                        timeLeftToBurn -= 1f * Time.deltaTime;
                    }
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
        else if (burnType == BurnType.ManeuverBurn)
        {
            if (!hasManeuverNode)
            {
                FindManeuverNode();
            }
            else
            {
                var timeWarp = GetTimeWarp();
                var burnStartTime = GetManeuverBurnStartTime(maneuverNode);
                var burnEndTime = GetManeuverBurnEndTime(maneuverNode);
                if (burnEndTime < 0)
                {
                    activeVessel.SetMainThrottle(0f);
                    SetBurnStatus(BurnStatus.Completed);
                }
                else if (burnStartTime < 0)
                {
                    if (timeWarp.CurrentRateIndex != 0)
                    {
                        timeWarp.SetRateIndex(0, false);
                    }
                    activeVessel.SetMainThrottle(1f);
                    SetBurnStatus(BurnStatus.InProgress);
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.B) && isDebug)
        {
            _isWindowOpen = !_isWindowOpen;
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
            if (!isPrerelease)
            {
                if (burnStatus == BurnStatus.None)
                {
                    _windowRect = GUILayout.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        _windowRect,
                        FillWindow,
                        "<color=orange>// Burn Controller " + ModVer + "</color>",
                        GUILayout.Width(500),
                        GUILayout.Height(250)
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
            else
            {
                if (burnStatus == BurnStatus.None)
                {
                    _windowRect = GUILayout.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        _windowRect,
                        FillWindow,
                        "<color=#1AA7EC>// Burn Controller " + ModVer + " " + prereleaseName + "</color>",
                        GUILayout.Width(600),
                        GUILayout.Height(250)
                    );
                }
                else
                {
                    _windowRect = GUILayout.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        _windowRect,
                        FillWindow,
                        "<color=#1AA7EC>// Burn Controller " + ModVer + " " + prereleaseName + "</color>",
                        GUILayout.Width(600),
                        GUILayout.Height(125)
                    );
                }
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
            if (constantThrottle)
            {
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
            }
            else
            {
                GUILayout.Label("From...");
                startThrustString = GUILayout.TextField(startThrustString, 3);
                if (int.TryParse(startThrustString, out startThrust))
                {
                    if (startThrust > 100)
                    {
                        startThrustString = GUILayout.TextField("100", 3);
                    }
                    else if (startThrust < 1)
                    {
                        startThrustString = GUILayout.TextField("1", 3);
                    }
                }
                if (startThrustString.Contains("."))
                {
                    startThrustString = GUILayout.TextField(startThrustString.Remove(1), 3);
                }
                GUILayout.Label("%");
                GUILayout.Label("To...");
                endThrustString = GUILayout.TextField(endThrustString, 3);
                if (int.TryParse(endThrustString, out endThrust))
                {
                    if (endThrust > 100)
                    {
                        endThrustString = GUILayout.TextField("100", 3);
                    }
                    else if (endThrust < 1)
                    {
                        endThrustString = GUILayout.TextField("1", 3);
                    }
                }
                if (endThrustString.Contains("."))
                {
                    endThrustString = GUILayout.TextField(endThrustString.Remove(1), 3);
                }
                GUILayout.Label("% Throttle");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            constantThrottle = GUILayout.Toggle(constantThrottle, " Use Constant Throttle?");
            GUILayout.Space(5);

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
                        //Code for a possible 0.9 prerelease update
                        GetCurrentStage();
                        //Code for a possible 0.9 prerelease update
                        if (!constantThrottle)
                        {
                            currentThrust = startThrust / 100f;
                        }
                        SetBurnStatus(BurnStatus.Waiting);
                        SetBurnType(BurnType.ManualBurn);
                    }
                }
            }
            if (GUILayout.Button("<size=30>SETUP MANEUVER BURN</size>", GUILayout.Height(40)))
            {
                SetBurnStatus(BurnStatus.Waiting);
                SetBurnType(BurnType.ManeuverBurn);
            }
        }
        else
        {
            if (burnType == BurnType.ManualBurn)
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
            }
            else if (burnType == BurnType.ManeuverBurn)
            {
                if (burnStatus == BurnStatus.Completed)
                {
                    GUILayout.Label("<color=#00FF00><size=30>Burn Complete!</size></color>");
                    GUILayout.Label("<size=20>Delete Your Maneuver Node To Return...</size>");
                    if (!hasManeuverNode)
                    {
                        SetBurnStatus(BurnStatus.None);
                        SetBurnType(BurnType.ManualBurn);
                    }
                }
            }

            if (burnStatus == BurnStatus.Waiting)
            {
                if (burnType == BurnType.ManualBurn)
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
                else if (burnType == BurnType.ManeuverBurn)
                {
                    GUILayout.Label("<size=20>Waiting To Reach Maneuver To Start Burn...</size>");
                    if (GUILayout.Button("<size=30>CANCEL MANEUVER BURN</size>", GUILayout.Height(40)))
                    {
                        if (hasManeuverNode)
                        {
                            hasManeuverNode = false;
                        }
                        SetBurnStatus(BurnStatus.None);
                        SetBurnType(BurnType.ManualBurn);
                    }
                }
            }
            if (burnStatus == BurnStatus.InProgress)
            {
                if (burnType == BurnType.ManualBurn)
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

                    //Code for a possible 0.9 prerelease update
                    if (freezeTimeLeft)
                    {
                        GUILayout.Label("Burn paused because engine ran out of fuel. Please wait...");
                    }
                    //Code for a possible 0.9 prerelease update

                    if (GUILayout.Button("<size=30>STOP BURN</size>", GUILayout.Height(40)))
                    {
                        SetBurnStatus(BurnStatus.Stopped);
                    }
                }
                else if (burnType == BurnType.ManeuverBurn)
                {
                    GUILayout.Label("<size=20>Burn Currently In Progress...</size>");
                    if (GUILayout.Button("<size=30>STOP MANEUVER BURN</size>", GUILayout.Height(40)))
                    {
                        if (hasManeuverNode)
                        {
                            hasManeuverNode = false;
                        }
                        SetBurnStatus(BurnStatus.Stopped);
                        SetBurnType(BurnType.ManualBurn);
                    }
                }
            }
            if (burnStatus == BurnStatus.Stopped)
            {
                GUILayout.Label("<color=#FF0000><size=30>Burn Stopped...</size></color>");

                if (GUILayout.Button("<size=30>RETURN</size>", GUILayout.Height(40)))
                {
                    //Code for a possible 0.9 prerelease update
                    if (freezeTimeLeft)
                    {
                        freezeTimeLeft = false;
                    }
                    //Code for a possible 0.9 prerelease update
                    getTimeLeft = false;
                    getTimeBefore = false;
                    if (startEngines)
                    {
                        hasActiveVehicle = false;
                        startEngines = false;
                    }
                    if (!constantThrottle)
                    {
                        startedThrustChanger = false;
                        StopCoroutine(ChangeThrustOverTime(0, 0, 0));
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

    private void SetBurnType(BurnType newType)
    {
        burnType = newType;
    }

    //A function that was added in the 0.8.0 update for finding the currently active manuever node
    private void FindManeuverNode()
    {
        if (!hasManeuverNode)
        {
            activeVessel = Vehicle.ActiveVesselVehicle;
            if (activeVessel != null)
            {
                activeVesselComponent = activeVessel.GetSimVessel();
                maneuverPlan = activeVessel.SimulationObject.FindComponent<ManeuverPlanComponent>();
                if (maneuverPlan == null)
                {
                    return;
                }
                else
                {
                    maneuverNode = maneuverPlan.ActiveNode;
                    if (maneuverNode == null)
                    {
                        return;
                    }
                    else
                    {
                        activeVesselComponent.SetAutopilotEnableDisable(true);
                        activeVessel.SetAutopilotMode(AutopilotMode.Maneuver);
                        hasManeuverNode = true;
                        maneuverPlan.OnManeuverNodesRemoved += OnNodeRemoved;
                    }
                }
            }
        }
    }

    private void OnNodeRemoved(List<ManeuverNodeData> guid)
    {
        if (hasManeuverNode)
        {
            hasManeuverNode = false;
        }
    }

    private static double GetManeuverBurnStartTime(ManeuverNodeData node)
    {
        var startTime = node.Time - Game.UniverseModel.UniversalTime;
        return startTime;
    }

    private static double GetManeuverBurnEndTime(ManeuverNodeData node)
    {
        var endTime = node.Time + node.BurnDuration - Game.UniverseModel.UniversalTime;
        return endTime;
    }

    private static TimeWarp GetTimeWarp()
    {
        return GameManager.Instance.Game.ViewController.TimeWarp;
    }

    //Change a value (currentThrust) for the current amount of throttle over time.
    private IEnumerator ChangeThrustOverTime(float startThrust, float endThrust, float burnDuration)
    {
        startedThrustChanger = true;
        for (float t = 0f; t < burnDuration; t += 1f * Time.deltaTime)
        {
            currentThrust = Mathf.Lerp(startThrust, endThrust, t / burnDuration);
            yield return null;
        }
        currentThrust = endThrust;
        startedThrustChanger = false;
    }

    //Code for getting the amount of delta V in the current stage (for possible 0.9 prerelease update)
    private double GetCurrentDeltaV()
    {
        var deltaV = currentStageInfo.DeltaVActual;
        return deltaV;
    }

    //Code for getting the current stage (for possible 0.9 prerelease update)
    private void GetCurrentStage()
    {
        List<DeltaVStageInfo> vesselStageInfo = GameManager.Instance.Game.ViewController.GetActiveVehicle(true).GetSimVessel(true).VesselDeltaV.StageInfo;
        currentStageInfo = null;
        for (int i = 0; i < vesselStageInfo.Count; i++)
        {
            if (vesselStageInfo[i].Stage == 1)
            {
                currentStageInfo = vesselStageInfo[i];
            }
        }
    }

    //Code for activating the next stage on a vehicle (for possible 0.9 prerelease update)
    private void ActivateStageIfNoEngines()
    {
        if (currentStageInfo != null)
        {
            if (currentStageInfo.Stage == 1 && currentStageInfo.EnginesInStage.Count == 0)
            {
                activeVessel.GetSimVessel(true).ActivateNextStage();
            }
            else
            {
                return;
            }
        }
    }

    //Code for staging the current vehicle until an engine is found in the current stage (for possible 0.9 prerelease update)
    private IEnumerator StageUntilEngineFound()
    {
        freezeTimeLeft = true;
        GetCurrentStage();
        yield return new WaitForEndOfFrame();
        while (currentStageInfo.EnginesInStage.Count == 0)
        {
            ActivateStageIfNoEngines();
            yield return new WaitForSeconds(2f);
            GetCurrentStage();
            yield return new WaitForSeconds(2f);
        }
        yield return new WaitForEndOfFrame();
        if (currentStageInfo.EnginesInStage.Count >= 1)
        {
            activeVessel.SetStage(true);
        }
        yield return new WaitForEndOfFrame();
        freezeTimeLeft = false;
        StopCoroutine(StageUntilEngineFound());
    }

    private enum BurnStatus
    {
        None,
        Waiting,
        InProgress,
        Completed,
        Stopped
    }

    private enum BurnType
    {
        ManualBurn,
        ManeuverBurn
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

    //Variables added in the 0.8.0 update
    private BurnType burnType;
    private bool hasManeuverNode = false;
    private ManeuverPlanComponent maneuverPlan;

    private ManeuverNodeData maneuverNode;
    private VesselComponent activeVesselComponent;

    //Variables added in the 0.8.1 update
    private bool constantThrottle = true;
    private string startThrustString = "100";
    private string endThrustString = "50";

    private int startThrust = 100;
    private int endThrust = 50;

    private float currentThrust = 0f;
    private bool startedThrustChanger = false;

    //Variables for a possible 0.9 prerelease update
    private bool freezeTimeLeft = false;
    private double currentDeltaV = 0.0f;
    private DeltaVStageInfo currentStageInfo = null;

    private bool isPrerelease;
    private string prereleaseName;
    private bool isDebug;
}
