namespace OrderSystem.Messaging;

/// <summary>
/// Result of handling one delivered event. <see cref="Abandon"/> is distinct from
/// <see cref="DeadLetter"/>: it means "not yet processable, redeliver later" (e.g. a
/// cross-topic precondition hasn't landed), not "poison message" — see
/// IEventSubscriber for how it's redelivered.
/// </summary>
public enum MessageOutcome
{
    Complete,
    Abandon,
    DeadLetter,
}
