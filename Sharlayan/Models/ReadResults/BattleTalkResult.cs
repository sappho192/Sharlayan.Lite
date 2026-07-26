namespace Sharlayan.Models.ReadResults {
    public sealed class BattleTalkResult {
        public BattleTalkResult(
            bool isAvailable,
            bool isVisible,
            string name,
            string text,
            long sequence) {
            this.IsAvailable = isAvailable;
            this.IsVisible = isVisible;
            this.Name = name ?? string.Empty;
            this.Text = text ?? string.Empty;
            this.Sequence = sequence;
        }

        public bool IsAvailable { get; }
        public bool IsVisible { get; }
        public string Name { get; }
        public string Text { get; }
        public long Sequence { get; }
    }
}
