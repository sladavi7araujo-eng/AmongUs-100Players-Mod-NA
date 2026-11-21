using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Api.Events;
using Impostor.Api.Events.Managers;
using Impostor.Api.Events.Meeting;
using Impostor.Api.Events.Player;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Plugins;
using Microsoft.Extensions.Logging;

namespace MeuModBR
{
    [ImpostorPlugin("com.davi.meumodbr")]
    public class ModPlugin : PluginBase
    {
        private readonly ILogger<ModPlugin> _logger;
        private readonly IEventManager _eventManager;
        private IDisposable? _unregister;

        public ModPlugin(ILogger<ModPlugin> logger, IEventManager eventManager)
        {
            _logger = logger;
            _eventManager = eventManager;
        }

        public override ValueTask EnableAsync()
        {
            _logger.LogInformation("MeuModBR Ativado!");
            _unregister = _eventManager.RegisterListener(new ModListener());
            return default;
        }

        public override ValueTask DisableAsync()
        {
            _unregister?.Dispose();
            return default;
        }
    }

    public class ModListener : IEventListener
    {
        private readonly Dictionary<int, string> _roles = new();
        private readonly HashSet<int> _rgb = new();
        private readonly HashSet<int> _shield = new();
        private CancellationTokenSource? _cts;

        private readonly Dictionary<string, string> _cores = new(StringComparer.OrdinalIgnoreCase)
        {
            {"banana","#F5F557"},{"amarelo","#F5F557"},{"vermelho","#C51111"},{"azul","#132ED1"},
            {"verde","#117F2D"},{"rosa","#ED54BA"},{"laranja","#EF7D0E"},{"roxo","#6B2FBB"},
            {"ciano","#38FEDC"},{"lima","#50EF39"},{"branco","#D6E0F0"},{"preto","#3F474E"}
        };
        private readonly List<string> _paleta = new() { "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF" };

        [EventListener]
        public async ValueTask OnChat(IPlayerChatEvent e)
        {
            var msg = e.Message.Trim();
            if (!msg.StartsWith("/")) return;
            e.IsCancelled = true;
            var p = e.ClientPlayer;
            var parts = msg.Split(' ', 2);
            var cmd = parts.ElementAtOrDefault(0)?.ToLower() ?? "";
            var arg = parts.ElementAtOrDefault(1)?.Trim() ?? "";

            if (e.Game.GameState != GameStates.NotStarted) return;

            if ((cmd == "/players100" || cmd == "/players") && p.IsHost)
            {
                int max = cmd == "/players100" ? 100 : (int.TryParse(arg, out int n) ? n : 0);
                if (max >= 4 && max <= 100)
                {
                    e.Game.Options.MaxPlayers = (byte)max;
                    e.Game.Options.NumImpostors = (byte)Math.Clamp(max / 6, 2, 15);
                    await e.Game.SyncSettingsAsync();
                    if (e.Game.Host?.Character != null)
                        await e.Game.Host.Character.SendChatAsync($"[NA Public] Sala ajustada para {max} players!");
                }
            }
            else if (cmd == "/name" && !string.IsNullOrEmpty(arg))
            {
                _rgb.Remove(p.Client.Id);
                await p.Character!.SetNameAsync(arg);
            }
            else if (cmd == "/namergb")
            {
                if (!string.IsNullOrEmpty(arg)) await p.Character!.SetNameAsync(arg);
                _rgb.Add(p.Client.Id);
                StartRgb(e.Game);
            }
            else if (cmd == "/color" && !string.IsNullOrEmpty(arg))
            {
                _rgb.Remove(p.Client.Id);
                string hex = _cores.TryGetValue(arg, out var c) ? c : (arg.StartsWith("#") ? arg : "");
                if (!string.IsNullOrEmpty(hex))
                {
                    string clean = CleanName(p.Character!.PlayerInfo.PlayerName);
                    await p.Character.SetNameAsync($"<color={hex}>{clean}</color>");
                }
            }
        }

        [EventListener]
        public async ValueTask OnStart(IGameStartedEvent e)
        {
            _roles.Clear();
            _shield.Clear();
            var imps = e.Game.Players.Where(p => p.Character?.PlayerInfo.IsImpostor == true).OrderBy(_ => Guid.NewGuid()).ToList();
            var crew = e.Game.Players.Where(p => p.Character?.PlayerInfo.IsImpostor == false).OrderBy(_ => Guid.NewGuid()).ToList();

            for (int i = 0; i < imps.Count; i++)
            {
                string r = (i % 2 == 0) ? "Metavibora" : "Assassino";
                _roles[imps[i].Client.Id] = r;
                await imps[i].Character!.SendChatToPlayerAsync($"SEU CARGO: {r.ToUpper()}");
            }
            if (crew.Count > 0) { _roles[crew.First().Client.Id] = "Xerife"; await crew.First().Character!.SendChatToPlayerAsync("SEU CARGO: XERIFE"); }
            if (crew.Count > 1) { _roles[crew.Skip(1).First().Client.Id] = "Medico"; await crew.Skip(1).First().Character!.SendChatToPlayerAsync("SEU CARGO: MEDICO"); }
            if (crew.Count > 2) { _roles[crew.Skip(2).First().Client.Id] = "Prefeito"; await crew.Skip(2).First().Character!.SendChatToPlayerAsync("SEU CARGO: PREFEITO"); }
        }

        [EventListener]
        public async ValueTask OnMurder(IPlayerMurderEvent e)
        {
            if (e.Victim == null) return;
            _roles.TryGetValue(e.ClientPlayer.Client.Id, out var role);
            if (_shield.Remove(e.Victim.Client.Id)) return;

            if (role == "Xerife" && !e.Victim.PlayerInfo.IsImpostor)
                await e.ClientPlayer.Character!.MurderPlayerAsync(e.ClientPlayer.Character);
            else if (role == "Metavibora")
                _ = Task.Run(async () => { await Task.Delay(5000); await e.Victim.NetworkTransform.SnapToAsync(new System.Numerics.Vector2(-999f, -999f)); });
        }

        [EventListener]
        public async ValueTask OnVent(IPlayerEnterVentEvent e)
        {
            var p = e.ClientPlayer;
            if (_roles.TryGetValue(p.Client.Id, out var r) && r == "Metavibora")
            {
                var t = e.Game.Players.FirstOrDefault(x => x.Client.Id != p.Client.Id && !x.Character!.PlayerInfo.IsDead);
                if (t?.Character != null && p.Character != null)
                {
                    string oName = p.Character.PlayerInfo.PlayerName;
                    var oCol = p.Character.PlayerInfo.CurrentOutfit.Color;
                    await p.Character.SetNameAsync(t.Character.PlayerInfo.PlayerName);
                    await p.Character.SetColorAsync(t.Character.PlayerInfo.CurrentOutfit.Color);
                    _ = Task.Run(async () => { await Task.Delay(20000); await p.Character.SetNameAsync(oName); await p.Character.SetColorAsync(oCol); });
                }
            }
        }

        [EventListener]
        public async ValueTask OnTask(IPlayerCompletedTaskEvent e)
        {
            if (_roles.TryGetValue(e.ClientPlayer.Client.Id, out var r) && r == "Medico")
            {
                var near = e.Game.Players.FirstOrDefault(x => x.Client.Id != e.ClientPlayer.Client.Id && !x.Character!.PlayerInfo.IsDead);
                if (near != null) _shield.Add(near.Client.Id);
            }
        }

        private void StartRgb(IGame g)
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;
            _cts = new CancellationTokenSource();
            var tok = _cts.Token;
            _ = Task.Run(async () => {
                int idx = 0;
                while (!tok.IsCancellationRequested && g.GameState == GameStates.NotStarted) {
                    string hex = _paleta[idx++ % _paleta.Count];
                    foreach (var p in g.Players)
                        if (_rgb.Contains(p.Client.Id) && p.Character != null)
                            await p.Character.SetNameAsync($"<color={hex}>{CleanName(p.Character.PlayerInfo.PlayerName)}</color>");
                    await Task.Delay(1000, tok);
                }
            }, tok);
        }

        private string CleanName(string n) => (n.StartsWith("<color=") && n.EndsWith("</color>")) ? n.Substring(n.IndexOf('>') + 1, n.LastIndexOf('<') - n.IndexOf('>') - 1) : n;
    }
}
