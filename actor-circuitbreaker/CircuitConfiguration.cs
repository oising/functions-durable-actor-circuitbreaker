using System;
using JetBrains.Annotations;

namespace Hollan.Function
{
    public class CircuitConfiguration
    {
        public readonly string ResourceId;
        public readonly TimeSpan BackOffDuration;

        public CircuitConfiguration([NotNull] string resourceId, TimeSpan backOffDuration)
        {
            this.ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            this.BackOffDuration = backOffDuration;
        }

        public override bool Equals(object obj)
        {
            return obj is CircuitConfiguration other &&
                   ResourceId == other.ResourceId &&
                   BackOffDuration.Equals(other.BackOffDuration);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ResourceId, BackOffDuration);
        }

        public void Deconstruct(out string resourceId, out TimeSpan backOffDuration)
        {
            resourceId = this.ResourceId;
            backOffDuration = this.BackOffDuration;
        }

        public static implicit operator (string resourceId, TimeSpan backOffDuration)(CircuitConfiguration value)
        {
            return (value.ResourceId, value.BackOffDuration);
        }

        public static implicit operator CircuitConfiguration((string resourceId, TimeSpan backOffDuration) value)
        {
            return new CircuitConfiguration(value.resourceId, value.backOffDuration);
        }
    }
}