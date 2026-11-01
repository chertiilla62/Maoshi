using Maoshi.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Плашка «X зовёт тебя в Maoshi» сверху экрана, когда приглашение Steam пришло при открытой игре.
    /// «Войти» — выйти из текущей игры (если есть) и зайти к другу; «Позже» — скрыть. Сама прячется через 30 с.
    /// </summary>
    public class InviteToast
    {
        public VisualElement Root { get; }

        private readonly Label _text;
        private ulong _lobbyId;
        private float _hideAt;

        public InviteToast()
        {
            Root = UIKit.Box("invite-toast");
            Root.pickingMode = PickingMode.Position;
            _text = UIKit.Text("", "invite-toast__text");
            Root.Add(_text);

            var buttons = UIKit.Row("invite-toast__buttons");
            buttons.Add(UIKit.Button("Войти", Accept, "btn--small", "btn--primary"));
            buttons.Add(UIKit.Button("Позже", Hide, "btn--small", "btn--ghost"));
            Root.Add(buttons);
            UIKit.Show(Root, false);

            SteamService.InviteReceived += Show;
        }

        public void Dispose() => SteamService.InviteReceived -= Show;

        private void Show(string friendName, ulong lobbyId)
        {
            _lobbyId = lobbyId;
            _hideAt = Time.unscaledTime + 30f;
            UIKit.SetText(_text, $"<b>{friendName}</b> зовёт тебя в Maoshi!");
            UIKit.Show(Root, true);
            Maoshi.Audio.AudioManager.Play("zone");
        }

        public void Tick()
        {
            if (Root.style.display == DisplayStyle.None) return;
            if (Time.unscaledTime > _hideAt) Hide();
        }

        private void Accept()
        {
            Hide();
            ConnectionManager.Instance?.AcceptInvite(_lobbyId);
        }

        private void Hide() => UIKit.Show(Root, false);
    }
}
