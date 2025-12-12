namespace DreamberdInterpreter
{
    public enum LifetimeKind
    {
        None,
        Lines,
        Seconds,
        Infinity
    }

    public readonly struct LifetimeSpecifier
    {
        public LifetimeKind Kind
        {
            get;
        }
        public double Value
        {
            get;
        }

        public LifetimeSpecifier(LifetimeKind kind, double value)
        {
            Kind = kind;
            Value = value;
        }

        public bool IsNone => Kind == LifetimeKind.None;

        public static LifetimeSpecifier None => new LifetimeSpecifier(LifetimeKind.None, 0);

        public static LifetimeSpecifier Lines(double lines) =>
            new LifetimeSpecifier(LifetimeKind.Lines, lines);

        public static LifetimeSpecifier Seconds(double seconds) =>
            new LifetimeSpecifier(LifetimeKind.Seconds, seconds);

        public static LifetimeSpecifier Infinity =>
            new LifetimeSpecifier(LifetimeKind.Infinity, 0);
    }
}
