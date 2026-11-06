using Dalamud.Configuration;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SubmarineIncomeTracker;

[Serializable]
public class IncomeRecord
{
    public DateTime Time { get; set; }
    public long Gil { get; set; }
    public Dictionary<string, int> Items { get; set; } = new();
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public List<IncomeRecord> History { get; set; } = new();
    public Vector2 HudPosition { get; set; } = new Vector2(300, 300);
    public bool ShowHud { get; set; } = true;
    public float HudScale { get; set; } = 1.0f;
}

public class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;

    public Configuration Config { get; }
    public Dictionary<string, int> CurrentItems { get; } = new();
    private bool isDisposed = false;
    private bool isDragging = false;

    public readonly Dictionary<string, int> NpcPrices = new()
    {
        { "\u6c89\u8239\u6212\u6307", 8000 }, { "\u6c89\u8239\u624b\u956f", 9000 }, { "\u6c89\u8239\u8033\u9970", 10000 }, { "\u6c89\u8239\u9879\u94fe", 13000 },
        { "\u4e0a\u7b49\u6c89\u8239\u6212\u6307", 27000 }, { "\u4e0a\u7b49\u6c89\u8239\u624b\u956f", 28500 }, { "\u4e0a\u7b49\u6c89\u8239\u8033\u9970", 30000 }, { "\u4e0a\u7b49\u6c89\u8239\u9879\u94fe", 34500 }
    };

    private readonly WindowSystem windowSystem = new("SubmarineIncomeTracker");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        mainWindow = new MainWindow(this);

        windowSystem.AddWindow(mainWindow);
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += DrawHud;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;

        ChatGui.ChatMessage += (message) => 
        {
            if (isDisposed) return;
            var text = message.Message.TextValue;
            if (!text.Contains("\u83b7\u5f97\u4e86") || !text.Contains("\u00d7")) return;

            var matchQty = Regex.Match(text, @"\u00d7\s*(\d+)");
            if (!matchQty.Success) return;
            var qty = int.Parse(matchQty.Groups[1].Value);

            bool foundLink = false;
            foreach (var payload in message.Message.Payloads)
            {
                if (payload is Dalamud.Game.Text.SeStringHandling.Payloads.ItemPayload itemPayload)
                {
                    var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    if (itemSheet != null)
                    {
                        var item = itemSheet.GetRow(itemPayload.ItemId);
                        if (item.RowId != 0)
                        {
                            AddItem(item.Name.ToString(), qty);
                            foundLink = true;
                        }
                    }
                }
            }

            if (!foundLink)
            {
                var matchName = Regex.Match(text, @"\u83b7\u5f97\u4e86[\u201c\u0022]?(.+?)[\u201d\u0022]?\u00d7");
                if (matchName.Success)
                {
                    var cleanName = Regex.Replace(matchName.Groups[1].Value, @"[^\u4e00-\u9fa5]", "");
                    AddItem(cleanName, qty);
                }
            }
        };

        var showStatsCmd = new CommandInfo((_, _) => {
            ToggleWindow();
            PrintCurrentSession();
        }) { HelpMessage = "\u6253\u5f00\u7edf\u8ba1\u754c\u9762" };

        CommandManager.AddHandler("/subincome", showStatsCmd);
        CommandManager.AddHandler("/sub", showStatsCmd);
        CommandManager.AddHandler("/subincome clear", new CommandInfo((_, _) => {
            CurrentItems.Clear();
            ChatGui.Print("\u5f53\u524d\u8bb0\u5f55\u5df2\u6e05\u7a7a\u3002");
        }));
    }

    private void ToggleWindow() => mainWindow.IsOpen = !mainWindow.IsOpen;

    private void DrawHud()
    {
        if (!Config.ShowHud || isDisposed) return;

        ImGui.SetNextWindowPos(Config.HudPosition, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

        ImGui.Begin("SubmarineHudWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing);

        var size = new Vector2(48, 48) * Config.HudScale;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("HudBtn", size);
        bool isHovered = ImGui.IsItemHovered();

        // 纯手工绘制 FF14 风格金币图标
        var center = pos + size / 2;
        float radius = size.X / 2;
        
        // 1. 阴影底
        drawList.AddCircleFilled(center + new Vector2(1, 1), radius, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.5f)), 32);
        
        // 2. 金色外圈
        uint goldColor = ImGui.GetColorU32(new Vector4(1.0f, 0.84f, 0.0f, isHovered ? 1.0f : 0.9f));
        drawList.AddCircleFilled(center, radius, goldColor, 32);
        
        // 3. 内圈暗色
        uint innerColor = ImGui.GetColorU32(new Vector4(0.8f, 0.6f, 0.0f, 1.0f));
        drawList.AddCircleFilled(center, radius * 0.85f, innerColor, 32);
        
        // 4. 绘制中间的文字 "G"
        var textSize = ImGui.CalcTextSize("G") * Config.HudScale;
        var textPos = center - textSize / 2;
        drawList.AddText(textPos + new Vector2(1, 1) * Config.HudScale, ImGui.GetColorU32(new Vector4(0, 0, 0, 1.0f)), "G");
        drawList.AddText(textPos, goldColor, "G");

        // 滚轮缩放
        if (isHovered && ImGui.GetIO().MouseWheel != 0)
        {
            Config.HudScale += ImGui.GetIO().MouseWheel * 0.1f;
            Config.HudScale = Math.Clamp(Config.HudScale, 0.5f, 3.0f);
            PluginInterface.SavePluginConfig(Config);
        }

        // 按住左键拖拽
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            isDragging = true;
            Config.HudPosition += ImGui.GetIO().MouseDelta;
            PluginInterface.SavePluginConfig(Config);
        }

        // 松开鼠标
        if (ImGui.IsItemDeactivated())
        {
            if (!isDragging)
            {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    ToggleWindow();
                else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                {
                    Config.ShowHud = false;
                    PluginInterface.SavePluginConfig(Config);
                    ChatGui.Print("\u60ac\u6d6e\u7a97\u5df2\u9690\u85cf\uff0c\u53ef\u5728\u4e3b\u754c\u9762\u91cd\u65b0\u5f00\u542f\u3002");
                }
            }
            isDragging = false;
        }

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void PrintCurrentSession()
    {
        if (CurrentItems.Count == 0) return;
        ChatGui.Print("=== \u6f5c\u6c34\u8247\u6536\u76ca\u7edf\u8ba1 ===");
        int totalCount = 0;
        long totalGil = 0;
        foreach (var kv in CurrentItems)
        {
            totalCount += kv.Value;
            if (NpcPrices.TryGetValue(kv.Key, out var price))
            {
                long sub = (long)price * kv.Value;
                totalGil += sub;
                ChatGui.Print("- " + kv.Key + " x" + kv.Value + " (" + sub.ToString("N0") + " \u91d1)");
            }
        }
        ChatGui.Print("\u5171\u8ba1 " + totalCount + " \u4ef6\u7269\u54c1\u3002");
        if (totalGil > 0) ChatGui.Print("\u6c89\u8239\u9996\u9970\u603b\u6536\u5165: " + totalGil.ToString("N0") + " \u91d1\u5e01\uff01");
    }

    private void AddItem(string name, int qty)
    {
        CurrentItems.TryGetValue(name, out var existing);
        CurrentItems[name] = existing + qty;
    }

    public void SaveCurrentSession()
    {
        if (CurrentItems.Count == 0) return;
        long totalGil = 0;
        foreach (var kv in CurrentItems)
            if (NpcPrices.ContainsKey(kv.Key)) totalGil += (long)NpcPrices[kv.Key] * kv.Value;

        Config.History.Add(new IncomeRecord {
            Time = DateTime.Now,
            Gil = totalGil,
            Items = new Dictionary<string, int>(CurrentItems)
        });
        PluginInterface.SavePluginConfig(Config);
        CurrentItems.Clear();
        ChatGui.Print("\u6536\u76ca\u5df2\u4fdd\u5b58\u5230\u5386\u53f2\u8bb0\u5f55\uff01");
    }

    public void Dispose()
    {
        isDisposed = true;
        CommandManager.RemoveHandler("/subincome");
        CommandManager.RemoveHandler("/sub");
        CommandManager.RemoveHandler("/subincome clear");
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= DrawHud;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        windowSystem.RemoveAllWindows();
    }
}

public class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("\u6f5c\u6c34\u8247\u6536\u76ca\u7edf\u8ba1###SubmarineUI")
    {
        this.plugin = plugin;
        Size = new Vector2(450, 550);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var config = plugin.Config;
        var now = DateTime.Now;
        var today = now.Date;
        
        int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        var startOfWeek = today.AddDays(-1 * diff);
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var startOfYear = new DateTime(now.Year, 1, 1);

        long todayGil = config.History.Where(x => x.Time.Date == today).Sum(x => x.Gil);
        long weekGil = config.History.Where(x => x.Time >= startOfWeek).Sum(x => x.Gil);
        long monthGil = config.History.Where(x => x.Time >= startOfMonth).Sum(x => x.Gil);
        long yearGil = config.History.Where(x => x.Time >= startOfYear).Sum(x => x.Gil);

        var goldColor = new Vector4(1.0f, 0.84f, 0.0f, 1.0f);
        var textColor = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
        
        bool showHud = config.ShowHud;
        if (ImGui.Checkbox("\u5728\u6e38\u620f\u4e3b\u754c\u9762\u663e\u793a\u60ac\u6d6e\u7a97", ref showHud))
        {
            config.ShowHud = showHud;
            Plugin.PluginInterface.SavePluginConfig(config); 
        }
        
        ImGui.SameLine();
        if (ImGui.Button("\u91cd\u7f6e\u60ac\u6d6e\u7a97\u4f4d\u7f6e"))
        {
            config.HudPosition = new Vector2(300, 300);
            config.HudScale = 1.0f;
            Plugin.PluginInterface.SavePluginConfig(config);
        }
        
        ImGui.Separator();

        ImGui.TextColored(goldColor, "\u6536\u76ca\u7edf\u8ba1\u6982\u89c8");
        if (ImGui.BeginTable("StatsTable", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(textColor, "\u4eca\u65e5\u6536\u76ca"); ImGui.TextColored(goldColor, todayGil.ToString("N0") + " \u91d1");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(textColor, "\u672c\u5468\u6536\u76ca"); ImGui.TextColored(goldColor, weekGil.ToString("N0") + " \u91d1");

            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(textColor, "\u672c\u6708\u6536\u76ca"); ImGui.TextColored(goldColor, monthGil.ToString("N0") + " \u91d1");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(textColor, "\u4eca\u5e74\u6536\u76ca"); ImGui.TextColored(goldColor, yearGil.ToString("N0") + " \u91d1");
            ImGui.EndTable();
        }

        ImGui.Spacing(); ImGui.Spacing();
        ImGui.TextColored(goldColor, "\u5f53\u524d\u6b63\u5728\u8bb0\u5f55\u7684\u7269\u54c1");
        ImGui.Separator();

        if (plugin.CurrentItems.Count == 0)
        {
            ImGui.TextDisabled("\u6682\u65e0\u8bb0\u5f55\uff0c\u53bb\u6536\u8247\u5427~");
        }
        else
        {
            long currentTotal = 0;
            if (ImGui.BeginTable("CurrentTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("\u7269\u54c1", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("\u6570\u91cf", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("\u4ef7\u503c", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                foreach (var kv in plugin.CurrentItems)
                {
                    ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.Text(kv.Key);
                    ImGui.TableSetColumnIndex(1); ImGui.Text(kv.Value.ToString());
                    ImGui.TableSetColumnIndex(2);
                    if (plugin.NpcPrices.TryGetValue(kv.Key, out var price))
                    {
                        long sub = (long)price * kv.Value;
                        currentTotal += sub;
                        ImGui.TextColored(goldColor, sub.ToString("N0"));
                    }
                    else ImGui.TextDisabled("\u975eNPC");
                }
                ImGui.EndTable();
            }
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "\u9884\u8ba1\u603b\u6536\u5165: " + currentTotal.ToString("N0") + " \u91d1");
            ImGui.Spacing();
            if (ImGui.Button("\u4fdd\u5b58\u672c\u6b21\u6536\u76ca", new Vector2(120, 30))) plugin.SaveCurrentSession();
            ImGui.SameLine();
            if (ImGui.Button("\u6e05\u7a7a\u5f53\u524d\u8bb0\u5f55", new Vector2(120, 30))) plugin.CurrentItems.Clear();
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("\u67e5\u770b\u6700\u8fd1\u4fdd\u5b58\u8bb0\u5f55"))
        {
            if (config.History.Count == 0) ImGui.TextDisabled("\u8fd8\u6ca1\u6709\u4fdd\u5b58\u8fc7\u4efb\u4f55\u8bb0\u5f55\u3002");
            else
            {
                var recent = config.History.OrderByDescending(x => x.Time).Take(10).ToList();
                if (ImGui.BeginTable("HistoryTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("\u65f6\u95f4", ImGuiTableColumnFlags.WidthFixed, 130);
                    ImGui.TableSetupColumn("\u6536\u76ca", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();
                    foreach (var record in recent)
                    {
                        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.Text(record.Time.ToString("MM-dd HH:mm"));
                        ImGui.TableSetColumnIndex(1); ImGui.TextColored(goldColor, record.Gil.ToString("N0") + " \u91d1");
                    }
                    ImGui.EndTable();
                }
            }
        }
    }
}
