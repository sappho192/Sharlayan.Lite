namespace Sharlayan.Models.ReadResults {
    public sealed class TalkResult {
        public TalkResult(bool isAvailable, string name, string text) {
            this.IsAvailable = isAvailable;
            this.Name = name ?? string.Empty;
            this.Text = text ?? string.Empty;
        }

        public bool IsAvailable { get; }
        public string Name { get; }
        public string Text { get; }

        internal static TalkResult Unavailable { get; } = new TalkResult(false, string.Empty, string.Empty);
    }
}
