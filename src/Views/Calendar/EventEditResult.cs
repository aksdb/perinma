using System;

namespace perinma.Views.Calendar;

public abstract record EventEditResult
{
    private EventEditResult() { }

    public sealed record Success(string EventId) : EventEditResult;

    public sealed record Cancelled : EventEditResult;

    public sealed record Error(Exception Exception) : EventEditResult;
}
