﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace SEScripts.ResourceExchanger2_4_0_187
{
    public class Program : MyGridProgram
    {
/// Resource Exchanger version 2.4.0 2018-07-22 for SE 1.187+
/// Made by Sinus32
/// http://steamcommunity.com/sharedfiles/filedetails/546221822
///
/// Attention! This script does not require any timer blocks and will run immediately.
/// If you want to stop it just switch the programmable block off.

/** Configuration section starts here. ******************************************/

/// Optional name of a group of blocks that will be affected by the script.
/// By default all blocks connected to grid are processed.
/// You can use this variable to limit the script to affect only certain blocks.
public string MANAGED_BLOCKS_GROUP = null;

/// Limit affected blocks to only these that are connected to the same ship/station as the
/// the programmable block. Set to true if blocks on ships connected by connectors
/// or rotors should not be affected.
public bool MY_GRID_ONLY = false;

/// Set this variable to false to disable exchanging uranium between reactors.
public bool ENABLE_BALANCING_REACTORS = true;

/// Set this variable to false to disable exchanging ore
/// between refineries and arc furnaces.
public bool ENABLE_DISTRIBUTING_ORE_IN_REFINERIES = true;

/// Set this variable to false to disable exchanging ore between drills and
/// to disable processing lights that indicates how much free space left in drills.
public bool ENABLE_DISTRIBUTING_ORE_IN_DRILLS = true;

/// Set this variable to false to disable exchanging ammunition between turrets and launchers.
public bool ENABLE_EXCHANGING_AMMUNITION_IN_TURRETS = true;

/// Set this variable to false to disable exchanging ice (and only ice - not bottles)
/// between oxygen generators.
public bool ENABLE_EXCHANGING_ICE_IN_OXYGEN_GENERATORS = true;

/// Set this variable to false to disable exchanging items in blocks of custom groups.
public bool ENABLE_EXCHANGING_ITEMS_IN_GROUPS = true;

/// Maximum number of items movements to do per each group of inventories.
/// This setting has significant impact to performance.
/// Bigger values may implicit server lags.
public int MAX_MOVEMENTS_PER_GROUP = 2;

/// Group of wide LCD screens that will act as debugger output for this script.
/// You can name this screens as you wish, but pay attention that
/// they will be used in alphabetical order according to their names.
public const string DISPLAY_LCD_GROUP = "Resource exchanger output";

/// Name of a group of lights that will be used as indicators of space left in drills.
/// Both Interior Light and Spotlight are supported.
/// The lights will change colors to tell you how much free space left:
/// White - All drills are connected to each other and they are empty.
/// Yellow - Drills are full in a half.
/// Red - Drills are almost full (95%).
/// Purple - Less than WARNING_LEVEL_IN_KILOLITERS_LEFT m3 of free space left.
/// Cyan - Some drills are not connected to each other.
/// You can change this colors a few lines below
///
/// Set this variable to null to disable this feature
public string DRILLS_PAYLOAD_LIGHTS_GROUP = "Payload indicators";

/// Amount of free space left in drills when the lights turn into purple
/// Measured in cubic meters
/// Default is 5 with means the lights from DRILLS_PAYLOAD_LIGHTS_GROUP will turn
/// into purple when there will be only 5,000 liters of free space
/// left in drills (or less)
public int WARNING_LEVEL_IN_CUBIC_METERS_LEFT = 5;

/// Configuration of lights colors
/// Values are in RGB format
/// Minimum value of any color component is 0, and maximum is 255
public Color COLOR_WHEN_DRILLS_ARE_EMPTY = new Color(255, 255, 255);
public Color COLOR_WHEN_DRILLS_ARE_FULL_IN_A_HALF = new Color(255, 255, 0);
public Color COLOR_WHEN_DRILLS_ARE_ALMOST_FULL = new Color(255, 0, 0);
public Color FIRST_WARNING_COLOR_WHEN_DRILLS_ARE_FULL = new Color(128, 0, 128);
public Color SECOND_WARNING_COLOR_WHEN_DRILLS_ARE_FULL = new Color(128, 0, 64);
public Color FIRST_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED = new Color(0, 128, 128);
public Color SECOND_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED = new Color(0, 64, 128);

/// Number of lines displayed on single LCD wide panel from DISPLAY_LCD_GROUP.
/// The default value of 17 is designated for panels with font size set to 1.0
public const int LINES_PER_DEBUG_SCREEN = 17;

/// Top priority item type to process in refineries and/or arc furnaces.
/// The script will move an item of this type to the first slot of a refinery or arc
/// furnace if it find that item in the refinery (or arc furnace) processing queue.
/// You can find definitions of other materials in line 1025 and below.
/// Set this variable to null to disable this feature
public readonly MyDefinitionId TopRefineryPriority = MyDefinitionId.Parse(OreType + "/" + IRON);

/// Lowest priority item type to process in refineries and/or arc furnaces.
/// The script will move an item of this type to the last slot of a refinery or arc
/// furnace if it find that item in the refinery (or arc furnace) processing queue.
/// You can find definitions of other materials in line 1025 and below.
/// Set this variable to null to disable this feature
public readonly MyDefinitionId LowestRefineryPriority = MyDefinitionId.Parse(OreType + "/" + STONE);

/// Number of last runs, from which average movements should be calculated.
public const int NUMBER_OF_AVERAGE_SAMPLES = 15;

/// Regular expression used to recognize groups
public readonly System.Text.RegularExpressions.Regex GROUP_TAG_PATTERN
    = new System.Text.RegularExpressions.Regex(@"\bGR\d{1,3}\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

/* Configuration section ends here. *********************************************/
// The rest of the code does the magic.

private const decimal SMALL_NUMBER = 0.000003M;
private StringBuilder _output;
private List<IMyTextPanel> _debugScreen;
private List<InventoryWrapper> _reactorsInventories;
private List<InventoryWrapper> _oxygenGeneratorsInventories;
private List<InventoryWrapper> _refineriesInventories;
private List<InventoryWrapper> _drillsInventories;
private List<InventoryWrapper> _turretsInventories;
private List<InventoryWrapper> _cargoContainersInventories;
private Dictionary<string, HashSet<InventoryWrapper>> _groups;
private HashSet<InventoryWrapper> _allGroupedInventories;
private HashSet<IMyCubeGrid> _allGrids;
private List<IMyLightingBlock> _drillsPayloadLights;
private readonly Dictionary<MyDefinitionId, ulong[]> _blockToGroupIdMap;
private readonly Dictionary<ulong[], string> _groupIdToNameMap;
private readonly HashSet<string> _missing;
private VRage.MyFixedPoint _drillsMaxVolume;
private VRage.MyFixedPoint _drillsCurrentVolume;
private int _numberOfNetworks;
private bool _notConnectedDrillsFound;
private int _cycleNumber = 0;
private int _movementsDone;
private readonly int[] _avgMovements = new int[NUMBER_OF_AVERAGE_SAMPLES];

public Program()
{
    _blockToGroupIdMap = new Dictionary<MyDefinitionId, ulong[]>(MyDefinitionId.Comparer);
    _groupIdToNameMap = new Dictionary<ulong[], string>(LongArrayComparer.Instance);
    _missing = new HashSet<string>();

    BuildItemInfoDict();
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

public void Save()
{ }

public void Main(string argument, UpdateType updateSource)
{
    CollectTerminals();
    PrintStatistics();

    ProcessReactors();
    ProcessRefineries();
    ProcessDrills();
    ProcessDrillsLights();
    ProcessTurrets();
    ProcessOxygenGenerators();
    ProcessGroups();

    PrintOnlineStatus();
    WriteOutput();
}

private void WriteOutput()
{
    if (_debugScreen.Count == 0)
        return;

    _debugScreen.Sort(MyTextPanelNameComparer.Instance);
    string[] lines = _output.ToString().Split(new char[] { '\n' },
        StringSplitOptions.RemoveEmptyEntries);

    int totalScreens = lines.Length + LINES_PER_DEBUG_SCREEN - 1;
    totalScreens /= LINES_PER_DEBUG_SCREEN;

    for (int i = 0; i < _debugScreen.Count; ++i)
    {
        var screen = _debugScreen[i];
        var sb = new StringBuilder();
        int firstLine = i * LINES_PER_DEBUG_SCREEN;
        for (int j = 0; j < LINES_PER_DEBUG_SCREEN && firstLine + j < lines.Length; ++j)
            sb.AppendLine(lines[firstLine + j].Trim());
        screen.WritePublicText(sb.ToString());
        screen.ShowPublicTextOnScreen();
    }
}

private void CollectTerminals()
{
    _output = new StringBuilder();
    _debugScreen = new List<IMyTextPanel>();
    _reactorsInventories = new List<InventoryWrapper>();
    _oxygenGeneratorsInventories = new List<InventoryWrapper>();
    _refineriesInventories = new List<InventoryWrapper>();
    _drillsInventories = new List<InventoryWrapper>();
    _turretsInventories = new List<InventoryWrapper>();
    _cargoContainersInventories = new List<InventoryWrapper>();
    _groups = new Dictionary<string, HashSet<InventoryWrapper>>();
    _allGroupedInventories = new HashSet<InventoryWrapper>();
    _allGrids = new HashSet<IMyCubeGrid>();
    _numberOfNetworks = 0;
    _drillsPayloadLights = new List<IMyLightingBlock>();
    _drillsMaxVolume = 0;
    _drillsCurrentVolume = 0;
    _notConnectedDrillsFound = false;
    _movementsDone = 0;

    var blocks = new List<IMyTerminalBlock>();

    if (String.IsNullOrEmpty(MANAGED_BLOCKS_GROUP))
    {
        GridTerminalSystem.GetBlocksOfType(blocks, MyTerminalBlockFilter);
    }
    else
    {
        var group = GridTerminalSystem.GetBlockGroupWithName(MANAGED_BLOCKS_GROUP);
        if (group == null)
            _output.Append("Error: a group ").Append(MANAGED_BLOCKS_GROUP).AppendLine(" has not been found");
        else
            group.GetBlocksOfType(blocks, MyTerminalBlockFilter);
    }

    foreach (var dt in blocks)
    {
        var collected = CollectContainer(dt as IMyCargoContainer)
            || CollectRefinery(dt as IMyRefinery)
            || CollectReactor(dt as IMyReactor)
            || CollectDrill(dt as IMyShipDrill)
            || CollectTurret(dt as IMyUserControllableGun)
            || CollectOxygenGenerator(dt as IMyGasGenerator);
    }

    if (!String.IsNullOrEmpty(DISPLAY_LCD_GROUP))
    {
        var group = GridTerminalSystem.GetBlockGroupWithName(DISPLAY_LCD_GROUP);
        if (group != null)
            group.GetBlocksOfType<IMyTextPanel>(_debugScreen, MyTerminalBlockFilter);
    }

    if (!String.IsNullOrEmpty(DRILLS_PAYLOAD_LIGHTS_GROUP))
    {
        var group = GridTerminalSystem.GetBlockGroupWithName(DRILLS_PAYLOAD_LIGHTS_GROUP);
        if (group != null)
            group.GetBlocksOfType<IMyLightingBlock>(_drillsPayloadLights, MyTerminalBlockFilter);
    }

    _output.Append("Resource exchanger. Blocks managed:");
    _output.Append(" reactors: ");
    _output.Append(CountOrNA(_reactorsInventories, ENABLE_BALANCING_REACTORS));
    _output.Append(", refineries: ");
    _output.Append(CountOrNA(_refineriesInventories, ENABLE_DISTRIBUTING_ORE_IN_REFINERIES));
    _output.AppendLine(",");
    _output.Append("oxygen gen.: ");
    _output.Append(CountOrNA(_oxygenGeneratorsInventories, ENABLE_EXCHANGING_ICE_IN_OXYGEN_GENERATORS));
    _output.Append(", drills: ");
    _output.Append(CountOrNA(_drillsInventories, ENABLE_DISTRIBUTING_ORE_IN_DRILLS));
    _output.Append(", turrets: ");
    _output.Append(CountOrNA(_turretsInventories, ENABLE_EXCHANGING_AMMUNITION_IN_TURRETS));
    _output.Append(", cargo cont.: ");
    _output.Append(CountOrNA(_cargoContainersInventories, ENABLE_EXCHANGING_ITEMS_IN_GROUPS));
    _output.Append(", custom groups: ");
    _output.Append(CountOrNA(_groups, ENABLE_EXCHANGING_ITEMS_IN_GROUPS));
    _output.AppendLine();
}

private string CountOrNA(System.Collections.ICollection collection, bool isEnabled)
{
    return isEnabled ? collection.Count.ToString() : "n/a";
}

private bool MyTerminalBlockFilter(IMyTerminalBlock myTerminalBlock)
{
    return myTerminalBlock.IsFunctional && (!MY_GRID_ONLY || myTerminalBlock.CubeGrid == Me.CubeGrid);
}

private bool CollectContainer(IMyCargoContainer myCargoContainer)
{
    if (myCargoContainer == null)
        return false;

    if (!ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
        return true;

    var inv = InventoryWrapper.Create(this, myCargoContainer);
    if (inv != null)
    {
        _cargoContainersInventories.Add(inv);
        _allGrids.Add(myCargoContainer.CubeGrid);
        AddToGroup(inv);
    }
    return true;
}

private bool CollectRefinery(IMyRefinery myRefinery)
{
    if (myRefinery == null)
        return false;

    if (!ENABLE_DISTRIBUTING_ORE_IN_REFINERIES || !myRefinery.UseConveyorSystem)
        return true;

    var inv = InventoryWrapper.Create(this, myRefinery);
    if (inv != null)
    {
        _refineriesInventories.Add(inv);
        _allGrids.Add(myRefinery.CubeGrid);
        if (ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
            AddToGroup(inv);
    }
    return true;
}

private bool CollectReactor(IMyReactor myReactor)
{
    if (myReactor == null)
        return false;

    if (!ENABLE_BALANCING_REACTORS || !myReactor.UseConveyorSystem)
        return true;

    var inv = InventoryWrapper.Create(this, myReactor);
    if (inv != null)
    {
        _reactorsInventories.Add(inv);
        _allGrids.Add(myReactor.CubeGrid);
        if (ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
            AddToGroup(inv);
    }
    return true;
}

private bool CollectDrill(IMyShipDrill myDrill)
{
    if (myDrill == null)
        return false;

    if (!ENABLE_DISTRIBUTING_ORE_IN_DRILLS || !myDrill.UseConveyorSystem)
        return true;

    var inv = InventoryWrapper.Create(this, myDrill);
    if (inv != null)
    {
        _drillsInventories.Add(inv);
        _allGrids.Add(myDrill.CubeGrid);
        if (ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
            AddToGroup(inv);

        _drillsMaxVolume += inv.Inventory.MaxVolume;
        _drillsCurrentVolume += inv.Inventory.CurrentVolume;
    }
    return true;
}

private bool CollectTurret(IMyUserControllableGun myTurret)
{
    if (myTurret == null)
        return false;

    if (!ENABLE_EXCHANGING_AMMUNITION_IN_TURRETS || myTurret is SpaceEngineers.Game.ModAPI.Ingame.IMyLargeInteriorTurret)
        return true;

    var inv = InventoryWrapper.Create(this, myTurret);
    if (inv != null)
    {
        _turretsInventories.Add(inv);
        _allGrids.Add(myTurret.CubeGrid);
        if (ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
            AddToGroup(inv);
    }
    return true;
}

private bool CollectOxygenGenerator(IMyGasGenerator myOxygenGenerator)
{
    if (myOxygenGenerator == null)
        return false;

    if (!ENABLE_EXCHANGING_ICE_IN_OXYGEN_GENERATORS)
        return true;

    var inv = InventoryWrapper.Create(this, myOxygenGenerator);
    if (inv != null)
    {
        _oxygenGeneratorsInventories.Add(inv);
        _allGrids.Add(myOxygenGenerator.CubeGrid);
        if (ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
            AddToGroup(inv);
    }
    return true;
}

private void AddToGroup(InventoryWrapper inv)
{
    foreach (System.Text.RegularExpressions.Match dt in GROUP_TAG_PATTERN.Matches(inv.Block.CustomName))
    {
        HashSet<InventoryWrapper> tmp;
        if (!_groups.TryGetValue(dt.Value, out tmp))
        {
            tmp = new HashSet<InventoryWrapper>();
            _groups.Add(dt.Value, tmp);
        }
        tmp.Add(inv);
        _allGroupedInventories.Add(inv);
    }
}

private List<ConveyorNetwork> FindConveyorNetworks(IEnumerable<InventoryWrapper> inventories, bool excludeGroups)
{
    var result = new List<ConveyorNetwork>();

    foreach (var wrp in inventories)
    {
        if (excludeGroups && _allGroupedInventories.Contains(wrp))
            continue;

        bool add = true;
        foreach (var network in result)
        {
            if (network.Inventories[0].Inventory.IsConnectedTo(wrp.Inventory)
                && wrp.Inventory.IsConnectedTo(network.Inventories[0].Inventory))
            {
                network.Inventories.Add(wrp);
                add = false;
                break;
            }
        }

        if (add)
        {
            var network = new ConveyorNetwork(result.Count + 1);
            network.Inventories.Add(wrp);
            result.Add(network);
        }
    }

    _numberOfNetworks += result.Count;
    return result;
}

private List<InventoryGroup> DivideByBlockType(ConveyorNetwork network)
{
    var groupMap = new Dictionary<string, InventoryGroup>();

    foreach (var inv in network.Inventories)
    {
        InventoryGroup group;
        if (!groupMap.TryGetValue(inv.GroupName, out group))
        {
            group = new InventoryGroup(groupMap.Count + 1, inv.GroupName);
            groupMap.Add(inv.GroupName, group);
        }
        group.Inventories.Add(inv);
    }

    var result = new List<InventoryGroup>(groupMap.Count);
    result.AddRange(groupMap.Values);
    return result;
}

private void ProcessReactors()
{
    if (!ENABLE_BALANCING_REACTORS)
    {
        _output.AppendLine("Balancing reactors is disabled.");
        Echo("Balancing reactors: OFF");
        return;
    }

    Echo("Balancing reactors: ON");

    if (_reactorsInventories.Count < 2)
    {
        _output.AppendLine("Balancing reactors. Not enough reactors found. Nothing to do.");
        return;
    }

    var conveyorNetworks = FindConveyorNetworks(_reactorsInventories, true);

    _output.Append("Balancing reactors. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    foreach (var network in conveyorNetworks)
        foreach (var group in DivideByBlockType(network))
            BalanceInventories(group.Inventories, network.No, group.No, group.Name);
}

private void ProcessRefineries()
{
    if (!ENABLE_DISTRIBUTING_ORE_IN_REFINERIES)
    {
        _output.AppendLine("Balancing refineries is disabled.");
        Echo("Balancing refineries: OFF");
        return;
    }

    Echo("Balancing refineries: ON");

    EnforceItemPriority(_refineriesInventories, TopRefineryPriority, LowestRefineryPriority);

    if (_refineriesInventories.Count < 2)
    {
        _output.AppendLine("Balancing refineries. Not enough refineries found. Nothing to do.");
        return;
    }

    var conveyorNetworks = FindConveyorNetworks(_refineriesInventories, true);

    _output.Append("Balancing refineries. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    foreach (var network in conveyorNetworks)
        foreach (var group in DivideByBlockType(network))
            BalanceInventories(group.Inventories, network.No, group.No, group.Name);
}

private void ProcessDrills()
{
    if (!ENABLE_DISTRIBUTING_ORE_IN_DRILLS)
    {
        _output.AppendLine("Balancing drills is disabled.");
        Echo("Balancing drills: OFF");
        return;
    }

    Echo("Balancing drills: ON");

    if (_drillsInventories.Count < 2)
    {
        _output.AppendLine("Balancing drills. Not enough drills found. Nothing to do.");
        return;
    }

    var conveyorNetworks = FindConveyorNetworks(_drillsInventories, true);
    _notConnectedDrillsFound = conveyorNetworks.Count > 1;

    _output.Append("Balancing drills. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    foreach (var network in conveyorNetworks)
        BalanceInventories(network.Inventories, network.No, 0, "drills");
}

private void ProcessTurrets()
{
    if (!ENABLE_EXCHANGING_AMMUNITION_IN_TURRETS)
    {
        _output.AppendLine("Exchanging ammunition in turrets is disabled.");
        Echo("Exchanging ammunition: OFF");
        return;
    }

    Echo("Exchanging ammunition: ON");

    if (_turretsInventories.Count < 2)
    {
        _output.AppendLine("Balancing turrets. Not enough turrets found. Nothing to do.");
        return;
    }

    var conveyorNetworks = FindConveyorNetworks(_turretsInventories, true);

    _output.Append("Balancing turrets. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    foreach (var network in conveyorNetworks)
        foreach (var group in DivideByBlockType(network))
            BalanceInventories(group.Inventories, network.No, group.No, group.Name);
}

private void ProcessOxygenGenerators()
{
    if (!ENABLE_EXCHANGING_ICE_IN_OXYGEN_GENERATORS)
    {
        _output.AppendLine("Exchanging ice in oxygen generators is disabled.");
        Echo("Exchanging ice: OFF");
        return;
    }

    Echo("Exchanging ice: ON");

    if (_oxygenGeneratorsInventories.Count < 2)
    {
        _output.AppendLine("Balancing ice in oxygen generators. Not enough generators found. Nothing to do.");
        return;
    }

    var conveyorNetworks = FindConveyorNetworks(_oxygenGeneratorsInventories, true);

    _output.Append("Balancing oxygen generators. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    foreach (var network in conveyorNetworks)
        BalanceInventories(network.Inventories, network.No, 0, "oxygen generators",
            item => item.Content.TypeId.ToString() == OreType);
}

private void ProcessGroups()
{
    if (!ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
    {
        _output.AppendLine("Exchanging items in groups is disabled.");
        Echo("Exchanging items in groups: OFF");
        return;
    }

    Echo("Exchanging items in groups: ON");

    if (_groups.Count < 1)
    {
        _output.AppendLine("Exchanging items in groups. No groups found. Nothing to do.");
        return;
    }

    foreach (var group in _groups)
    {
        var conveyorNetworks = FindConveyorNetworks(group.Value, false);

        _output.Append("Balancing custom group '");
        _output.Append(group.Key);
        _output.Append("'. Conveyor networks found: ");
        _output.Append(conveyorNetworks.Count);
        _output.AppendLine();

        foreach (var network in conveyorNetworks)
            BalanceInventories(network.Inventories, network.No, 0, group.Key);
    }
}

private void ProcessDrillsLights()
{
    if (!ENABLE_DISTRIBUTING_ORE_IN_DRILLS)
    {
        _output.AppendLine("Setting color of drills payload indicators is disabled.");
        return;
    }

    if (_drillsPayloadLights.Count == 0)
    {
        _output.AppendLine("Setting color of drills payload indicators. Not enough lights found. Nothing to do.");
        return;
    }

    _output.AppendLine("Setting color of drills payload indicators.");

    Color color;

    if (_notConnectedDrillsFound)
    {
        _output.AppendLine("Not all drills are connected.");

        if (_cycleNumber % 2 == 0)
            color = FIRST_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED;
        else
            color = SECOND_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED;
        Echo("Some drills are not connected");
    }
    else
    {
        float p;
        if (_drillsMaxVolume > 0)
        {
            p = (float)_drillsCurrentVolume * 1000.0f;
            p /= (float)_drillsMaxVolume;

            _output.Append("Drills space usage: ");
            var percentString = (p / 10.0f).ToString("F1");
            _output.Append(percentString);
            _output.AppendLine("%");
            string echo = String.Concat("Drils payload: ", percentString, "%");

            if ((_drillsMaxVolume - _drillsCurrentVolume) < WARNING_LEVEL_IN_CUBIC_METERS_LEFT)
            {
                if (_cycleNumber % 2 == 0)
                {
                    color = FIRST_WARNING_COLOR_WHEN_DRILLS_ARE_FULL;
                    echo += " ! !";
                }
                else
                {
                    color = SECOND_WARNING_COLOR_WHEN_DRILLS_ARE_FULL;
                    echo += "  ! !";
                }
            }
            else
            {
                Color c1, c2;
                float m1, m2;

                if (p < 500.0f)
                {
                    c1 = COLOR_WHEN_DRILLS_ARE_EMPTY;
                    c2 = COLOR_WHEN_DRILLS_ARE_FULL_IN_A_HALF;
                    m2 = p / 500.0f;
                    m1 = 1.0f - m2;
                }
                else
                {
                    c1 = COLOR_WHEN_DRILLS_ARE_ALMOST_FULL;
                    c2 = COLOR_WHEN_DRILLS_ARE_FULL_IN_A_HALF;
                    m1 = (p - 500.0f) / 450.0f;
                    if (m1 > 1.0f)
                        m1 = 1.0f;
                    m2 = 1.0f - m1;
                }

                float r = c1.R * m1 + c2.R * m2;
                float g = c1.G * m1 + c2.G * m2;
                float b = c1.B * m1 + c2.B * m2;

                if (r > 255.0f)
                    r = 255.0f;
                else if (r < 0.0f)
                    r = 0.0f;

                if (g > 255.0f)
                    g = 255.0f;
                else if (g < 0.0f)
                    g = 0.0f;

                if (b > 255.0f)
                    b = 255.0f;
                else if (b < 0.0f)
                    b = 0.0f;

                color = new Color((int)r, (int)g, (int)b);
            }

            Echo(echo);
        }
        else
        {
            color = COLOR_WHEN_DRILLS_ARE_EMPTY;
        }
    }

    _output.Append("Drills payload indicators lights color: ");
    _output.Append(color);
    _output.AppendLine();

    foreach (IMyLightingBlock light in _drillsPayloadLights)
    {
        Color currentColor = light.GetValue<Color>("Color");
        if (currentColor != color)
            light.SetValue<Color>("Color", color);
    }

    _output.Append("Color of ");
    _output.Append(_drillsPayloadLights.Count);
    _output.AppendLine(" drills payload indicators has been set.");
}

private void PrintStatistics()
{
    var blocksAffected = _reactorsInventories.Count
        + _oxygenGeneratorsInventories.Count
        + _refineriesInventories.Count
        + _drillsInventories.Count
        + _turretsInventories.Count
        + _cargoContainersInventories.Count;
    Echo("Blocks affected: " + blocksAffected);
    Echo("Grids connected: " + _allGrids.Count + (MY_GRID_ONLY ? " (MGO)" : ""));
}

private void PrintOnlineStatus()
{
    Echo("Conveyor networks: " + _numberOfNetworks);
    _avgMovements[_cycleNumber % NUMBER_OF_AVERAGE_SAMPLES] = _movementsDone;
    var samples = _cycleNumber + 1 < NUMBER_OF_AVERAGE_SAMPLES
        ? _cycleNumber + 1 : NUMBER_OF_AVERAGE_SAMPLES;
    double avg = 0;
    for (int i = 0; i < samples; ++i)
        avg += _avgMovements[i];
    avg /= samples;
    Echo("Avg. movements: " + avg.ToString("F2") + " (last " + samples + " runs)");

    if (_missing.Count > 0)
        Echo("Error: missing volume information for " + String.Join(", ", _missing));

    var tab = new char[42];
    for (int i = 0; i < 42; ++i)
    {
        char c = ' ';
        if (i % 21 == 1)
            c = '|';
        else if (i % 7 < 4)
            c = '·';
        tab[41 - (i + _cycleNumber) % 42] = c;
    }
    Echo(new String(tab));
    ++_cycleNumber;
}

private void EnforceItemPriority(List<InventoryWrapper> group, MyDefinitionId topPriority, MyDefinitionId lowestPriority)
{
    if (topPriority == null && lowestPriority == null)
        return;

    foreach (var inv in group)
    {
        var items = inv.Inventory.GetItems();
        if (items.Count < 2)
            continue;

        if (topPriority != null && !MyDefinitionId.FromContent(items[0].Content).Equals(topPriority))
        {
            for (int i = 1; i < items.Count; ++i)
            {
                var item = items[i];
                if (MyDefinitionId.FromContent(item.Content).Equals(topPriority))
                {
                    _output.Append("Moving ");
                    _output.Append(topPriority.SubtypeName);
                    _output.Append(" from ");
                    _output.Append(i + 1);
                    _output.Append(" slot to first slot of ");
                    _output.Append(inv.Block.CustomName);
                    _output.AppendLine();
                    inv.TransferItemTo(inv, i, 0, false, item.Amount);
                    ++_movementsDone;
                    break;
                }
            }
        }

        if (lowestPriority != null && !MyDefinitionId.FromContent(items[items.Count - 1].Content).Equals(lowestPriority))
        {
            for (int i = items.Count - 2; i >= 0; --i)
            {
                var item = items[i];
                if (MyDefinitionId.FromContent(item.Content).Equals(lowestPriority))
                {
                    _output.Append("Moving ");
                    _output.Append(lowestPriority.SubtypeName);
                    _output.Append(" from ");
                    _output.Append(i + 1);
                    _output.Append(" slot to last slot of ");
                    _output.Append(inv.Block.CustomName);
                    _output.AppendLine();
                    inv.TransferItemTo(inv, i, items.Count, false, item.Amount);
                    ++_movementsDone;
                    break;
                }
            }
        }
    }
}

private void BalanceInventories(List<InventoryWrapper> group, int networkNumber, int groupNumber,
    string groupName, Func<IMyInventoryItem, bool> filter = null)
{
    if (group.Count < 2)
    {
        _output.Append("Cannot balance conveyor network ").Append(networkNumber + 1)
            .Append(" group ").Append(groupNumber + 1).Append(" \"").Append(groupName)
            .AppendLine("\"")
            .AppendLine("  because there is only one inventory.");
        return; // nothing to do
    }

    if (filter != null)
    {
        foreach (var wrp in group)
            wrp.LoadVolume().FilterItems(filter, _output).CalculatePercent();
    }
    else
    {
        foreach (var wrp in group)
            wrp.LoadVolume().CalculatePercent();
    }

    group.Sort(InventoryWrapperComparer.Instance);

    var last = group[group.Count - 1];

    if (last.CurrentVolume < SMALL_NUMBER)
    {
        _output.Append("Cannot balance conveyor network ").Append(networkNumber + 1)
            .Append(" group ").Append(groupNumber + 1).Append(" \"").Append(groupName)
            .AppendLine("\"")
            .AppendLine("  because of lack of items in it.");
        return; // nothing to do
    }

    _output.Append("Balancing conveyor network ").Append(networkNumber + 1)
        .Append(" group ").Append(groupNumber + 1)
        .Append(" \"").Append(groupName).AppendLine("\"...");

    for (int i = 0; i < MAX_MOVEMENTS_PER_GROUP && i < group.Count / 2; ++i)
    {
        var inv1 = group[i];
        var inv2 = group[group.Count - i - 1];

        decimal toMove;
        if (inv1.MaxVolume == inv2.MaxVolume)
        {
            toMove = (inv2.CurrentVolume - inv1.CurrentVolume) / 2.0M;
        }
        else
        {
            toMove = (inv2.CurrentVolume * inv1.MaxVolume
                - inv1.CurrentVolume * inv2.MaxVolume)
                / (inv1.MaxVolume + inv2.MaxVolume);
        }

        _output.Append("Inv. 1 vol: ").Append(inv1.CurrentVolume.ToString("F6")).Append("; ");
        _output.Append("Inv. 2 vol: ").Append(inv2.CurrentVolume.ToString("F6")).Append("; ");
        _output.Append("To move: ").Append(toMove.ToString("F6")).AppendLine();

        if (toMove < 0.0M)
            throw new InvalidOperationException("Something went wrong with calculations:"
                + " volumeDiff is " + toMove);

        if (toMove < SMALL_NUMBER)
            continue;

        MoveVolume(inv2, inv1, (VRage.MyFixedPoint)toMove, filter);
    }
}

private VRage.MyFixedPoint MoveVolume(InventoryWrapper from, InventoryWrapper to,
    VRage.MyFixedPoint volumeAmountToMove, Func<IMyInventoryItem, bool> filter)
{
    if (volumeAmountToMove == 0)
        return volumeAmountToMove;

    if (volumeAmountToMove < 0)
        throw new ArgumentException("Invalid volume amount", "volumeAmount");

    _output.Append("Move ");
    _output.Append(volumeAmountToMove);
    _output.Append(" l. from ");
    _output.Append(from.Block.CustomName);
    _output.Append(" to ");
    _output.AppendLine(to.Block.CustomName);
    List<IMyInventoryItem> itemsFrom = from.Inventory.GetItems();

    for (int i = itemsFrom.Count - 1; i >= 0; --i)
    {
        IMyInventoryItem item = itemsFrom[i];

        if (filter != null && !filter(item))
            continue;

        var key = MyDefinitionId.FromContent(item.Content);
        var data = ItemInfo.Get(key, _output);
        if (data == null)
        {
            _missing.Add(key.ToString());
            continue;
        }

        decimal amountToMoveRaw = (decimal)volumeAmountToMove * 1000M / data.Volume;
        VRage.MyFixedPoint amountToMove;

        if (data.IsSingleItem)
            amountToMove = (VRage.MyFixedPoint)((int)(amountToMoveRaw + 0.1M));
        else
            amountToMove = (VRage.MyFixedPoint)amountToMoveRaw;

        if (amountToMove == 0)
            continue;

        List<IMyInventoryItem> itemsTo = to.Inventory.GetItems();
        int targetItemIndex = 0;
        while (targetItemIndex < itemsTo.Count)
        {
            IMyInventoryItem item2 = itemsTo[targetItemIndex];
            if (MyDefinitionId.FromContent(item2.Content).Equals(key))
                break;
            ++targetItemIndex;
        }

        decimal itemVolume;
        bool success;
        if (amountToMove <= item.Amount)
        {
            itemVolume = (decimal)amountToMove * data.Volume / 1000M;
            success = from.TransferItemTo(to, i, targetItemIndex, true, amountToMove);
            ++_movementsDone;
            _output.Append("Move ");
            _output.Append(amountToMove);
            _output.Append(" -> ");
            _output.AppendLine(success ? "success" : "failure");
        }
        else
        {
            itemVolume = (decimal)item.Amount * data.Volume / 1000M;
            success = from.TransferItemTo(to, i, targetItemIndex, true, item.Amount);
            ++_movementsDone;
            _output.Append("Move all ");
            _output.Append(item.Amount);
            _output.Append(" -> ");
            _output.AppendLine(success ? "success" : "failure");
        }

        if (success)
            volumeAmountToMove -= (VRage.MyFixedPoint)itemVolume;
        if (volumeAmountToMove < (VRage.MyFixedPoint)SMALL_NUMBER)
            return volumeAmountToMove;
    }

    _output.Append("Cannot move ");
    _output.Append(volumeAmountToMove);
    _output.AppendLine(" l.");

    return volumeAmountToMove;
}

private const string OreType = "MyObjectBuilder_Ore";
private const string IngotType = "MyObjectBuilder_Ingot";
private const string ComponentType = "MyObjectBuilder_Component";
private const string AmmoMagazineType = "MyObjectBuilder_AmmoMagazine";
private const string PhysicalGunObjectType = "MyObjectBuilder_PhysicalGunObject";
private const string OxygenContainerObjectType = "MyObjectBuilder_OxygenContainerObject";
private const string GasContainerObjectType = "MyObjectBuilder_GasContainerObject";
private const string ModelComponentType = "MyObjectBuilder_ModelComponent";
private const string TreeObjectType = "MyObjectBuilder_TreeObject";

private const string COBALT = "Cobalt";
private const string GOLD = "Gold";
private const string ICE = "Ice";
private const string IRON = "Iron";
private const string MAGNESIUM = "Magnesium";
private const string NICKEL = "Nickel";
private const string ORGANIC = "Organic";
private const string PLATINUM = "Platinum";
private const string SCRAP = "Scrap";
private const string SILICON = "Silicon";
private const string SILVER = "Silver";
private const string STONE = "Stone";
private const string URANIUM = "Uranium";

private void BuildItemInfoDict()
{
    ItemInfo.Add(AmmoMagazineType, "250shell", 128M, 64M, true, true); // CSD Battlecannon
    ItemInfo.Add(AmmoMagazineType, "300mmShell_AP", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "300mmShell_HE", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "88hekc", 16M, 16M, true, true); // CSD Battlecannon
    ItemInfo.Add(AmmoMagazineType, "88shell", 16M, 16M, true, true); // CSD Battlecannon
    ItemInfo.Add(AmmoMagazineType, "900mmShell_AP", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "900mmShell_HE", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "Aden30x113", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "AFmagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "AZ_Missile_AA", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "AZ_Missile200mm", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "BatteryCannonAmmo1", 50M, 50M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "BatteryCannonAmmo2", 200M, 200M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "BigBertha", 3600M, 2800M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "BlasterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
    ItemInfo.Add(AmmoMagazineType, "Bofors40mm", 36M, 28M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "ConcreteMix", 2M, 2M, true, true); // Concrete Tool - placing voxels in survival
    ItemInfo.Add(AmmoMagazineType, "Eikester_Missile120mm", 25M, 30M, true, true); // (DX11) Small Missile Turret
    ItemInfo.Add(AmmoMagazineType, "Eikester_Nuke", 1800M, 8836M, true, true); // (DX11) Nuke Launcher [WiP]
    ItemInfo.Add(AmmoMagazineType, "EmergencyBlasterMagazine", 0.45M, 0.2M, true, true); // Independent Survival
    ItemInfo.Add(AmmoMagazineType, "Flak130mm", 2M, 3M, true, true); // [SEI] Weapon Pack DX11
    ItemInfo.Add(AmmoMagazineType, "Flak200mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
    ItemInfo.Add(AmmoMagazineType, "Flak500mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
    ItemInfo.Add(AmmoMagazineType, "HDTCannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "HighDamageGatlingAmmo", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    ItemInfo.Add(AmmoMagazineType, "ISM_FusionAmmo", 35M, 10M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "ISM_GrendelAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "ISM_Hellfire", 45M, 60M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "ISM_LongbowAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "ISM_MinigunAmmo", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "ISMNeedles", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "ISMTracer", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
    ItemInfo.Add(AmmoMagazineType, "LargeKlingonCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "LargeShipShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "LargeShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "LaserAmmo", 0.001M, 0.01M, true, true); // (DX11)Laser Turret
    ItemInfo.Add(AmmoMagazineType, "LaserArrayFlakMagazine", 45M, 30M, true, true); // White Dwarf - Directed Energy Platform [DX11]
    ItemInfo.Add(AmmoMagazineType, "LaserArrayShellMagazine", 45M, 120M, true, true); // White Dwarf - Directed Energy Platform [DX11]
    ItemInfo.Add(AmmoMagazineType, "Liquid Naquadah", 0.25M, 0.1M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(AmmoMagazineType, "LittleDavid", 360M, 280M, true, true); // Battle Cannon and Turrets (DX11)
    ItemInfo.Add(AmmoMagazineType, "MinotaurAmmo", 360M, 128M, true, true); // (DX11)Minotaur Cannon
    ItemInfo.Add(AmmoMagazineType, "Missile200mm", 45M, 60M, true, true); // Space Engineers
    ItemInfo.Add(AmmoMagazineType, "MK1CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "MK2CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "MK3CannonMagazineAP", 100M, 100M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "MK3CannonMagazineHE", 300M, 100M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "NATO_25x184mm", 35M, 16M, true, true); // Space Engineers
    ItemInfo.Add(AmmoMagazineType, "NATO_5p56x45mm", 0.45M, 0.2M, true, true); // Space Engineers
    ItemInfo.Add(AmmoMagazineType, "NiFeDUSlugMagazineLZM", 45M, 50M, true, true); // Large Ship Railguns
    ItemInfo.Add(AmmoMagazineType, "Phaser2Charge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "Phaser2ChargeLarge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "PhaserCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "PhaserChargeLarge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "Plasma_Hydrogen", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
    ItemInfo.Add(AmmoMagazineType, "PlasmaCutterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
    ItemInfo.Add(AmmoMagazineType, "RB_NATO_125x920mm", 875M, 160M, true, true); // RB Weapon Collection [DX11]
    ItemInfo.Add(AmmoMagazineType, "RB_Rocket100mm", 11.25M, 15M, true, true); // RB Weapon Collection [DX11]
    ItemInfo.Add(AmmoMagazineType, "RB_Rocket400mm", 180M, 240M, true, true); // RB Weapon Collection [DX11]
    ItemInfo.Add(AmmoMagazineType, "RomulanCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "RomulanChargeLarge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "SmallKlingonCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
    ItemInfo.Add(AmmoMagazineType, "SmallShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "SmallShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "SniperRoundHighSpeedLowDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    ItemInfo.Add(AmmoMagazineType, "SniperRoundHighSpeedLowDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    ItemInfo.Add(AmmoMagazineType, "SniperRoundLowSpeedHighDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    ItemInfo.Add(AmmoMagazineType, "SniperRoundLowSpeedHighDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    ItemInfo.Add(AmmoMagazineType, "TankCannonAmmoSEM4", 35M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(AmmoMagazineType, "TelionAF_PMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "TelionAMMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
    ItemInfo.Add(AmmoMagazineType, "TritiumMissile", 72M, 60M, true, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
    ItemInfo.Add(AmmoMagazineType, "TritiumShot", 3M, 3M, true, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
    ItemInfo.Add(AmmoMagazineType, "TungstenBolt", 4812M, 250M, true, true); // (DX11)Mass Driver
    ItemInfo.Add(AmmoMagazineType, "Vulcan20x102", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)

    ItemInfo.Add(ComponentType, "AdvancedReactorBundle", 50M, 20M, true, true); // Tiered Thorium Reactors and Refinery (new)
    ItemInfo.Add(ComponentType, "AlloyPlate", 30M, 3M, true, true); // Industrial Centrifuge (stable/dev)
    ItemInfo.Add(ComponentType, "ampHD", 10M, 15.5M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
    ItemInfo.Add(ComponentType, "ArcFuel", 2M, 0.627M, true, true); // Arc Reactor Pack [DX-11 Ready]
    ItemInfo.Add(ComponentType, "ArcReactorcomponent", 312M, 100M, true, true); // Arc Reactor Pack [DX-11 Ready]
    ItemInfo.Add(ComponentType, "AzimuthSupercharger", 10M, 9M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    ItemInfo.Add(ComponentType, "BulletproofGlass", 15M, 8M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Canvas", 15M, 8M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Computer", 0.2M, 1M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "ConductorMagnets", 900M, 200M, true, true); // (DX11)Mass Driver
    ItemInfo.Add(ComponentType, "Construction", 8M, 2M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "DenseSteelPlate", 200M, 30M, true, true); // Arc Reactor Pack [DX-11 Ready]
    ItemInfo.Add(ComponentType, "Detector", 5M, 6M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Display", 8M, 6M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Drone", 200M, 60M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(ComponentType, "DT-MiniSolarCell", 0.08M, 0.2M, true, true); // }DT{ Modpack
    ItemInfo.Add(ComponentType, "Explosives", 2M, 2M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Girder", 6M, 2M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "GrapheneAerogelFilling", 0.160M, 2.9166M, true, true); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(ComponentType, "GrapheneNanotubes", 0.01M, 0.1944M, true, true); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(ComponentType, "GraphenePlate", 6.66M, 0.54M, true, true); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(ComponentType, "GraphenePowerCell", 25M, 45M, true, true); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(ComponentType, "GrapheneSolarCell", 4M, 12M, true, true); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(ComponentType, "GravityGenerator", 800M, 200M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "InteriorPlate", 3M, 5M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "LargeTube", 25M, 38M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Magna", 100M, 15M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
    ItemInfo.Add(ComponentType, "MagnetronComponent", 50M, 20M, true, true); // Deuterium Fusion Reactors
    ItemInfo.Add(ComponentType, "Magno", 10M, 5.5M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
    ItemInfo.Add(ComponentType, "Medical", 150M, 160M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "MetalGrid", 6M, 15M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Mg_FuelCell", 15M, 16M, true, true); // Ripptide's CW & EE Continued (DX11)
    ItemInfo.Add(ComponentType, "Motor", 24M, 8M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Naquadah", 100M, 10M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(ComponentType, "Neutronium", 500M, 5M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(ComponentType, "PowerCell", 25M, 45M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "productioncontrolcomponent", 40M, 15M, true, true); // (DX11) Double Sided Upgrade Modules
    ItemInfo.Add(ComponentType, "RadioCommunication", 8M, 70M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Reactor", 25M, 8M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Scrap", 2M, 2M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    ItemInfo.Add(ComponentType, "SmallTube", 4M, 2M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "SolarCell", 8M, 20M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "SteelPlate", 20M, 3M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Superconductor", 15M, 8M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "Thrust", 40M, 10M, true, true); // Space Engineers
    ItemInfo.Add(ComponentType, "TractorHD", 1500M, 200M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
    ItemInfo.Add(ComponentType, "Trinium", 100M, 10M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(ComponentType, "Tritium", 3M, 3M, true, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
    ItemInfo.Add(ComponentType, "TVSI_DiamondGlass", 40M, 8M, true, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
    ItemInfo.Add(ComponentType, "WaterTankComponent", 200M, 160M, true, true); // Industrial Centrifuge (stable/dev)
    ItemInfo.Add(ComponentType, "ZPM", 50M, 60M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)

    ItemInfo.Add(GasContainerObjectType, "GrapheneHydrogenBottle", 20M, 100M, true, false); // Graphene Armor [Beta]
    ItemInfo.Add(GasContainerObjectType, "HydrogenBottle", 30M, 120M, true, false); // Space Engineers

    ItemInfo.Add(IngotType, "Carbon", 1M, 0.052M, false, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
    ItemInfo.Add(IngotType, "Cobalt", 1M, 0.112M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Gold", 1M, 0.052M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "HeavyH2OIngot", 2M, 1M, false, true); // Deuterium Fusion Reactors
    ItemInfo.Add(IngotType, "Iron", 1M, 0.127M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "LiquidHelium", 1M, 4.6M, false, true); // (DX11)Mass Driver
    ItemInfo.Add(IngotType, "Magmatite", 100M, 37M, false, true); // Stone and Gravel to Metal Ingots (DX 11)
    ItemInfo.Add(IngotType, "Magnesium", 1M, 0.575M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Naquadah", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(IngotType, "Neutronium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(IngotType, "Nickel", 1M, 0.112M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Platinum", 1M, 0.047M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Silicon", 1M, 0.429M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Silver", 1M, 0.095M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "Stone", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "SuitFuel", 0.0003M, 0.052M, false, true); // Independent Survival
    ItemInfo.Add(IngotType, "SuitRTGPellet", 1.0M, 0.052M, false, true); // Independent Survival
    ItemInfo.Add(IngotType, "ThoriumIngot", 3M, 20M, false, true); // Tiered Thorium Reactors and Refinery (new)
    ItemInfo.Add(IngotType, "Trinium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(IngotType, "Tungsten", 1M, 0.52M, false, true); // (DX11)Mass Driver
    ItemInfo.Add(IngotType, "Uranium", 1M, 0.052M, false, true); // Space Engineers
    ItemInfo.Add(IngotType, "v2HydrogenGas", 2.1656M, 0.43M, false, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
    ItemInfo.Add(IngotType, "v2OxygenGas", 4.664M, 0.9M, false, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2

    ItemInfo.Add(ModelComponentType, "AstronautBackpack", 5M, 60M, true, true); // Space Engineers

    ItemInfo.Add(OreType, "Akimotoite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Autunite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Carbon", 1M, 0.37M, false, true); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(OreType, "Carnotite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Cattierite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Chlorargyrite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Cobalt", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Cohenite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Cooperite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Dense Iron", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Deuterium", 1.5M, 0.5M, false, true); // Deuterium Fusion Reactors
    ItemInfo.Add(OreType, "Dolomite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Electrum", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Galena", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Glaucodot", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Gold", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Hapkeite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Heazlewoodite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Helium", 1M, 5.6M, false, true); // (DX11)Mass Driver
    ItemInfo.Add(OreType, "Ice", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Icy Stone", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Iron", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Kamacite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Magnesium", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Naquadah", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(OreType, "Neutronium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(OreType, "Nickel", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Niggliite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Olivine", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Organic", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Petzite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Platinum", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Porphyry", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Pyrite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Quartz", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Silicon", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Silver", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Sinoite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Sperrylite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Stone", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Taenite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Thorium", 1M, 0.9M, false, true); // Tiered Thorium Reactors and Refinery (new)
    ItemInfo.Add(OreType, "Trinium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(OreType, "Tungsten", 1M, 0.47M, false, true); // (DX11)Mass Driver
    ItemInfo.Add(OreType, "Uraniaurite", 1M, 0.37M, false, true); // Better Stone v6.9.2
    ItemInfo.Add(OreType, "Uranium", 1M, 0.37M, false, true); // Space Engineers
    ItemInfo.Add(OreType, "Wadsleyite", 1M, 0.37M, false, true); // Better Stone v6.9.2

    ItemInfo.Add(OxygenContainerObjectType, "GrapheneOxygenBottle", 20M, 100M, true, false); // Graphene Armor [Core] [Beta]
    ItemInfo.Add(OxygenContainerObjectType, "OxygenBottle", 30M, 120M, true, false); // Space Engineers

    ItemInfo.Add(PhysicalGunObjectType, "AngleGrinder2Item", 3M, 20M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "AngleGrinder3Item", 3M, 20M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "AngleGrinder4Item", 3M, 20M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "AngleGrinderItem", 3M, 20M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "AutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "CubePlacerItem", 1M, 1M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "EmergencyBlasterItem", 3M, 14M, true, false); // Independent Survival
    ItemInfo.Add(PhysicalGunObjectType, "GoodAIRewardPunishmentTool", 0.1M, 1M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "HandDrill2Item", 22M, 25M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "HandDrill3Item", 22M, 25M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "HandDrill4Item", 22M, 25M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "HandDrillItem", 22M, 25M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "P90", 3M, 12M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(PhysicalGunObjectType, "PhysicalConcreteTool", 5M, 15M, true, false); // Concrete Tool - placing voxels in survival
    ItemInfo.Add(PhysicalGunObjectType, "PreciseAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "RapidFireAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "Staff", 3M, 16M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)
    ItemInfo.Add(PhysicalGunObjectType, "TritiumAutomaticRifleItem", 6M, 21M, true, false); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
    ItemInfo.Add(PhysicalGunObjectType, "UltimateAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "Welder2Item", 5M, 8M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "Welder3Item", 5M, 8M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "Welder4Item", 5M, 8M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "WelderItem", 5M, 8M, true, false); // Space Engineers
    ItemInfo.Add(PhysicalGunObjectType, "Zat", 3M, 12M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)

    ItemInfo.Add(TreeObjectType, "DeadBushMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "DesertBushMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "DesertTree", 1500M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "DesertTreeDead", 1500M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "DesertTreeDeadMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "DesertTreeMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "LeafBushMedium_var1", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "LeafBushMedium_var2", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "LeafTree", 1500M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "LeafTreeMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "PineBushMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "PineTree", 1500M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "PineTreeMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "PineTreeSnow", 1500M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "PineTreeSnowMedium", 1300M, 8000M, true, true); // Space Engineers
    ItemInfo.Add(TreeObjectType, "SnowPineBushMedium", 1300M, 8000M, true, true); // Space Engineers
}

private string FindInvGroupName(MyDefinitionId def, IMyInventory inv)
{
    const string MY_OBJECT_BUILDER = "MyObjectBuilder_";

    ulong[] groupId;
    if (_blockToGroupIdMap.TryGetValue(def, out groupId))
        return _groupIdToNameMap[groupId];

    groupId = new ulong[ItemInfo.ID_LENGTH];
    foreach (var kv in ItemInfo.Dict)
    {
        if (inv.CanItemsBeAdded(-1, kv.Key))
        {
            for (int i = 0; i < ItemInfo.ID_LENGTH; ++i)
                groupId[i] |= kv.Value.Id[i];
        }
    }

    string result;
    if (_groupIdToNameMap.TryGetValue(groupId, out result))
    {
        _blockToGroupIdMap.Add(def, groupId);
        return result;
    }
    else
    {
        var fullType = def.ToString();
        result = fullType.StartsWith(MY_OBJECT_BUILDER)
        ? '$' + fullType.Substring(MY_OBJECT_BUILDER.Length)
        : fullType;

        _groupIdToNameMap.Add(groupId, result);
        _blockToGroupIdMap.Add(def, groupId);
        return result;
    }
}

private class ItemInfo
{
    public const int ID_LENGTH = 4;
    private static readonly Dictionary<MyDefinitionId, ItemInfo> _itemInfoDict;

    static ItemInfo()
    {
        _itemInfoDict = new Dictionary<MyDefinitionId, ItemInfo>(MyDefinitionId.Comparer);
    }

    private ItemInfo(int itemInfoNo, decimal mass, decimal volume, bool isSingleItem, bool isStackable)
    {
        Id = new ulong[ID_LENGTH];
        Id[itemInfoNo >> 6] = 1ul << (itemInfoNo & 0x3F);
        Mass = mass;
        Volume = volume;
        IsSingleItem = isSingleItem;
        IsStackable = isStackable;
    }

    public static Dictionary<MyDefinitionId, ItemInfo> Dict
    {
        get { return _itemInfoDict; }
    }

    public readonly ulong[] Id;
    public readonly decimal Mass;
    public readonly decimal Volume;
    public readonly bool IsSingleItem;
    public readonly bool IsStackable;

    public static void Add(string mainType, string subtype,
        decimal mass, decimal volume, bool isSingleItem, bool isStackable)
    {
        MyDefinitionId key;
        var fullType = String.Concat(mainType, '/', subtype);
        if (!MyDefinitionId.TryParse(fullType, out key))
            return;
        var value = new ItemInfo(_itemInfoDict.Count, mass, volume, isSingleItem, isStackable);
        try
        {
            _itemInfoDict.Add(key, value);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("Item info " + fullType + " already added", ex);
        }
    }

    public static ItemInfo Get(MyDefinitionId key, StringBuilder output)
    {
        ItemInfo data;
        if (_itemInfoDict.TryGetValue(key, out data))
            return data;

        output.Append("Volume to amount ratio for ");
        output.Append(key);
        output.AppendLine(" is not known.");
        return null;
    }
}

private class InventoryWrapperComparer : IComparer<InventoryWrapper>
{
    public static readonly IComparer<InventoryWrapper> Instance = new InventoryWrapperComparer();

    public int Compare(InventoryWrapper x, InventoryWrapper y)
    {
        return Decimal.Compare(x.Percent, y.Percent);
    }
}

private class MyTextPanelNameComparer : IComparer<IMyTextPanel>
{
    public static readonly IComparer<IMyTextPanel> Instance = new MyTextPanelNameComparer();

    public int Compare(IMyTextPanel x, IMyTextPanel y)
    {
        return String.Compare(x.CustomName, y.CustomName, true);
    }
}

private class LongArrayComparer : IEqualityComparer<ulong[]>
{
    public static readonly IEqualityComparer<ulong[]> Instance = new LongArrayComparer();

    public bool Equals(ulong[] x, ulong[] y)
    {
        //return System.Linq.Enumerable.SequenceEqual<ulong>(x, y);
        if (x.Length != y.Length)
            return false;
        for (int i = 0; i < x.Length; ++i)
            if (x[i] != y[i])
                return false;
        return true;
    }

    public int GetHashCode(ulong[] obj)
    {
        ulong result = 0ul;
        for (int i = 0; i < obj.Length; ++i)
            result += obj[i];
        return (int)result + (int)(result >> 32);
    }
}

private class InventoryWrapper
{
    public IMyTerminalBlock Block;
    public IMyInventory Inventory;
    public string GroupName;
    public decimal CurrentVolume;
    public decimal MaxVolume;
    public decimal Percent;

    public static InventoryWrapper Create(Program prog, IMyTerminalBlock block)
    {
        var inv = block.GetInventory(0);
        if (inv != null && inv.MaxVolume > 0)
        {
            var result = new InventoryWrapper();
            result.Block = block;
            result.Inventory = inv;
            result.GroupName = prog.FindInvGroupName(block.BlockDefinition, inv);

            return result;
        }
        return null;
    }

    public InventoryWrapper LoadVolume()
    {
        CurrentVolume = (decimal)Inventory.CurrentVolume;
        MaxVolume = (decimal)Inventory.MaxVolume;
        return this;
    }

    public InventoryWrapper FilterItems(Func<IMyInventoryItem, bool> filter, StringBuilder output)
    {
        decimal volumeBlocked = 0.0M;
        foreach (var item in Inventory.GetItems())
        {
            if (filter(item))
                continue;

            var key = MyDefinitionId.FromContent(item.Content);
            var data = ItemInfo.Get(key, output);
            if (data == null)
                continue;

            volumeBlocked += (decimal)item.Amount * data.Volume / 1000M;
        }

        if (volumeBlocked > 0.0M)
        {
            CurrentVolume -= volumeBlocked;
            MaxVolume -= volumeBlocked;
            output.Append("volumeBlocked ");
            output.AppendLine(volumeBlocked.ToString("N6"));
        }
        return this;
    }

    public void CalculatePercent()
    {
        Percent = CurrentVolume / MaxVolume;
    }

    public bool TransferItemTo(InventoryWrapper dst, int sourceItemIndex, int? targetItemIndex = null, bool? stackIfPossible = null, VRage.MyFixedPoint? amount = null)
    {
        return Inventory.TransferItemTo(dst.Inventory, sourceItemIndex, targetItemIndex, stackIfPossible, amount);
    }
}

private class ConveyorNetwork
{
    public int No;
    public List<InventoryWrapper> Inventories;

    public ConveyorNetwork(int no)
    {
        No = no;
        Inventories = new List<InventoryWrapper>();
    }
}

private class InventoryGroup
{
    public int No;
    public string Name;
    public List<InventoryWrapper> Inventories;

    public InventoryGroup(int no, string name)
    {
        No = no;
        Name = name;
        Inventories = new List<InventoryWrapper>();
    }
}
    }

    internal class ReferencedTypes
    {
        private static Type[] ImplicitIngameNamespacesFromTypes = new Type[] {
            typeof(Object),
            typeof(StringBuilder),
            typeof(IEnumerable),
            typeof(IEnumerable<>),
            typeof(Vector2),
            typeof(Game),
            typeof(ITerminalAction),
            typeof(IMyGridTerminalSystem),
            typeof(MyModelComponent),
            typeof(IMyComponentAggregate),
            typeof(ListReader<>),
            typeof(MyObjectBuilder_FactionDefinition),
            typeof(IMyCubeBlock),
            typeof(IMyAirVent),
        };
    }
}
