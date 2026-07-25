namespace Sharlayan.Models.ReadResults {
    public enum TalkSource {
        None,
        Current,
        Last,
    }

    public sealed class TalkResult {
        public TalkResult(bool isAvailable, string name, string text) {
            this.IsAvailable = isAvailable;
            this.Name = name ?? string.Empty;
            this.Text = text ?? string.Empty;
            this.Source = TalkSource.None;
            this.IsVisible = false;
        }

        public TalkResult(bool isAvailable, string name, string text, TalkSource source, bool isVisible) {
            this.IsAvailable = isAvailable;
            this.Name = name ?? string.Empty;
            this.Text = text ?? string.Empty;
            this.Source = source;
            this.IsVisible = isVisible;
        }

        public bool IsAvailable { get; }
        public string Name { get; }
        public string Text { get; }
        public TalkSource Source { get; }
        public bool IsVisible { get; }

        internal static TalkResult Unavailable { get; } = new TalkResult(false, string.Empty, string.Empty, TalkSource.None, false);
    }
}
