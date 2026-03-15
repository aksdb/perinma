using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Ical.Net.DataTypes;
using NodaTime;
using NodaTime.Text;
using perinma.Models;
using perinma.Utils;
using Calendar = Ical.Net.Calendar;
using Duration = NodaTime.Duration;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

namespace perinma.Services.Google;

/// <summary>
/// Google Calendar implementation of ICalendarProvider.
/// </summary>
public class GoogleCalendarProvider(
    IGoogleCalendarService googleCalendarService,
    CredentialManagerService credentialManager,
    IClock? clock = null)
    : ICalendarProvider
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private static ModelExtension<GoogleEvent> GoogleEventExtension = new();

    /// <inheritdoc/>
    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, Interval timeRange) =>
        ParseCalendarEventsInternal(rawEvents, timeRange);

    private List<CalendarEvent> ParseCalendarEventsInternal(List<RawEvent> rawEvents, Interval timeRange)
    {
        var googleEvents = rawEvents
            .Select(e => (e.Reference, Event: NewtonsoftJsonSerializer.Instance.Deserialize<Event>(e.RawData)))
            .Where(t => t.Event != null && t.Event.Status != "cancelled")
            .ToList();

        var overrides = googleEvents
            .Where(t => !string.IsNullOrEmpty(t.Event.RecurringEventId))
            .ToList();

        return googleEvents
            .Where(t => string.IsNullOrEmpty(t.Event.RecurringEventId)) // Main events (regular or master recurring)
            .SelectMany(t =>
            {
                if (t.Event.Recurrence is { Count: > 0 })
                {
                    // Generate occurrences for recurring events
                    return DetermineOccurrences(t.Event, timeRange)
                        .Where(occurrenceStart => !overrides.Any(ov =>
                            ov.Event.RecurringEventId == t.Event.Id &&
                            ParseGoogleDateTime(ov.Event.OriginalStartTime) == occurrenceStart))
                        .Select(occurrenceStart => MapToCalendarEvent(t.Reference, t.Event, occurrenceStart));
                }

                // Regular non-recurring event
                return [MapToCalendarEvent(t.Reference, t.Event, null)];
            })
            .Concat(overrides.Select(ov =>
                MapToCalendarEvent(ov.Reference, ov.Event, null))) // Include the overrides themselves
            .Where(ce => ce.StartTime.ToInstant() <= timeRange.End && ce.EndTime.ToInstant() >= timeRange.Start)
            .ToList();
    }

    private CalendarEvent MapToCalendarEvent(EventReference reference, GoogleEvent googleEvent,
        Instant? occurrenceStart)
    {
        var start = ParseGoogleDateTime(googleEvent.Start) ?? throw new InvalidOperationException("event without start time");;
        var end = ParseGoogleDateTime(googleEvent.End) ?? throw new InvalidOperationException("event without end time");;

        if (occurrenceStart.HasValue)
        {
            // This is a bit more complicated since we can't rely on the end time anymore.
            // We have to calculate the original duration and shift the whole event timeframe.

            var duration = end.Minus(start);
            start = occurrenceStart.Value;
            end = start.Plus(duration);
        }
        
        string? timeZone = null;
        if (!string.IsNullOrEmpty(googleEvent.Start.TimeZone))
            timeZone = googleEvent.Start.TimeZone;
        // If the start is represented as a date instead of a datetime, it's apparently full-day.
        bool fullDay = !string.IsNullOrEmpty(googleEvent.Start.Date);

        var relevantStatus = googleEvent.Attendees
            ?.FirstOrDefault(a => a.Self == true)
            ?.ResponseStatus;

        var extensions = new ModelExtensions();
        extensions.Set(GoogleEventExtension, googleEvent);
        if (fullDay)
            extensions.Set(CalendarEventExtensions.FullDay, true);
        if (timeZone is not null)
            extensions.Set(CalendarEventExtensions.TimeZone, timeZone);
        if (!string.IsNullOrEmpty(googleEvent.Location))
            extensions.Set(CalendarEventExtensions.Location, googleEvent.Location);
        if (!string.IsNullOrEmpty(googleEvent.Description))
            extensions.Set(CalendarEventExtensions.Description, new RichText.HTML(googleEvent.Description));
        if (googleEvent.Attachments?.Count > 0)
            extensions.Set(CalendarEventExtensions.Attachments, googleEvent.Attachments.Select(a =>
                new CalendarEventAttachment
                {
                    Title = a.Title,
                    Url = a.FileUrl,
                }).ToList());
        if (googleEvent.ConferenceData != null)
            extensions.Set(CalendarEventExtensions.Conference, new CalendarEventConference
            {
                Name = googleEvent.ConferenceData.ConferenceSolution.Name,
                EntryPoints = googleEvent.ConferenceData.EntryPoints
                    .OrderBy(ep => ep.EntryPointType)
                    .Reverse()
                    .Select(ep => new CalendarEventConference.EntryPoint
                    {
                        Label = ep.EntryPointType,
                        Uri = ep.Uri,
                    }).ToList()
            });

        if (googleEvent.Attendees is { Count: > 0 })
            extensions.Set(CalendarEventExtensions.Participants, googleEvent.Attendees.Select(a =>
                new CalendarEventParticipant
                {
                    Email = a.Email,
                    Name = a.DisplayName,
                    Status = MapResponseStatus(a.ResponseStatus),
                    IsOrganizer = a.Organizer ?? false
                }).ToList());

        var selfAttendee = googleEvent.Attendees?.FirstOrDefault(a => a.Self == true);
        var canRespond = selfAttendee is { ResponseStatus: not null } && !(selfAttendee.Organizer ?? false);
        if (canRespond && selfAttendee != null)
        {
            var responseStatus = MapResponseStatus(selfAttendee.ResponseStatus ?? "needsAction");
            var accountId = reference.Calendar.Account.Id.ToString();
            var calendarId = reference.Calendar.ExternalId ?? string.Empty;
            var eventId = reference.ExternalId ?? string.Empty;
            var participation = new Participation
            {
                CurrentState = responseStatus,
                Actions = new ParticipationActions
                {
                    Accept = async () =>
                        await this.RespondToEventAsync(accountId, calendarId, eventId, string.Empty, "accepted"),
                    Decline = async () =>
                        await this.RespondToEventAsync(accountId, calendarId, eventId, string.Empty, "declined"),
                    Tentative = async () =>
                        await this.RespondToEventAsync(accountId, calendarId, eventId, string.Empty, "tentative")
                }
            };
            extensions.Set(CalendarEventExtensions.Participation, participation);
        }

        var localStartTime = start.ToLocalDateTime();
        var localEndTime = end.ToLocalDateTime();

        if (fullDay)
        {
            // We need to "round" the duration to midnight in our zone.
            localStartTime = localStartTime.Date.AtMidnight();
            localEndTime = localEndTime.Date.AtMidnight();
        }

        return new CalendarEvent
        {
            Reference = reference,
            Title = googleEvent.Summary,
            StartTime = localStartTime,
            EndTime = localEndTime,
            ChangedAt = googleEvent.UpdatedDateTimeOffset?.DateTime,
            ResponseStatus = MapResponseStatus(relevantStatus),
            Extensions = extensions,
        };
    }

    private static EventResponseStatus MapResponseStatus(string? status) => status?.ToLower() switch
    {
        "needsaction" => EventResponseStatus.NeedsAction,
        "declined" => EventResponseStatus.Declined,
        "tentative" => EventResponseStatus.Tentative,
        "accepted" => EventResponseStatus.Accepted,
        _ => EventResponseStatus.None
    };

    /// <inheritdoc/>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Fetch calendars from Google
        var result = await googleCalendarService.GetCalendarsAsync(service, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var calendars = result.Calendars.Select<CalendarListEntry, ProviderCalendar>(c => new ProviderCalendar
        {
            ExternalId = c.Id,
            Name = c.Summary ?? "Unnamed Calendar",
            Color = c.BackgroundColor,
            Selected = c.Selected == true,
            Deleted = c.Deleted == true,
            Data = new()
            {
                { "rawData", new DataAttribute.JsonText(NewtonsoftJsonSerializer.Instance.Serialize(c)) }
            }
        }).ToList();

        return new CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<EventSyncResult> GetEventsAsync(
        string accountId,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Fetch events from Google
        var result =
            await googleCalendarService.GetEventsAsync(service, calendarExternalId, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var events = new List<ProviderEvent>();

        foreach (var evt in result.Events)
        {
            var providerEvent = ConvertGoogleEvent(evt);
            if (providerEvent != null)
            {
                events.Add(providerEvent);
            }
        }

        return new EventSyncResult
        {
            Events = events,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
            if (googleCredentials == null)
            {
                return false;
            }

            var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken);

            // Try to fetch calendar list as a connection test
            await googleCalendarService.GetCalendarsAsync(service, null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google Calendar connection test failed: {ex.Message}");
            return false;
        }
    }

    private static ProviderEvent? ConvertGoogleEvent(GoogleEvent evt)
    {
        var isOverride = !string.IsNullOrEmpty(evt.RecurringEventId);

        // For non-override cancelled events, mark as deleted
        if (!isOverride && evt.Status == "cancelled")
        {
            return new ProviderEvent
            {
                ExternalId = evt.Id,
                Title = evt.Summary,
                Status = evt.Status,
                Deleted = true,
                RawData = NewtonsoftJsonSerializer.Instance.Serialize(evt)
            };
        }

        Instant? startTime = null;
        Instant? endTime = null;
        Instant? originalStartTime = null;

        // Handle override events
        if (isOverride)
        {
            // Parse OriginalStartTime (when override replaces)
            originalStartTime = ParseGoogleDateTime(evt.OriginalStartTime);

            if (evt.Status == "cancelled")
            {
                // Cancelled override - use OriginalStartTime
                startTime = originalStartTime;
                endTime = originalStartTime;
            }
            else
            {
                // Modified override - parse actual start/end
                startTime = ParseGoogleDateTime(evt.Start);
                endTime = ParseGoogleDateTime(evt.End);

                // Ensure OriginalStartTime is within bounds
                if (originalStartTime.HasValue && startTime.HasValue && endTime.HasValue)
                {
                    if (originalStartTime.Value < startTime.Value)
                    {
                        startTime = originalStartTime;
                    }
                    else if (originalStartTime.Value > endTime.Value)
                    {
                        endTime = originalStartTime;
                    }
                }
            }
        }
        else
        {
            // Regular events
            if (evt.Start == null || evt.End == null)
                return null;

            startTime = ParseGoogleDateTime(evt.Start);
            endTime = ParseGoogleDateTime(evt.End);

            // For recurring events, calculate recurrence end time
            if (evt.Recurrence is { Count: > 0 } && startTime.HasValue && endTime.HasValue)
            {
                var recurrenceEndTime = RecurrenceParser.GetRecurrenceEndTime(
                    evt.Recurrence,
                    startTime.Value.ToDateTimeUtc(),
                    endTime.Value.ToDateTimeUtc());

                if (recurrenceEndTime.HasValue)
                {
                    // TODO merge local recurrence calculations into the RecurrenceParser and
                    //   make it ZonedDateTime aware
                    endTime = Instant.FromDateTimeUtc(recurrenceEndTime.Value.ToUniversalTime());
                }
            }
        }

        return new ProviderEvent
        {
            ExternalId = evt.Id,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = startTime,
            EndTime = endTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = evt.RecurringEventId,
            OriginalStartTime = originalStartTime,
            RawData = NewtonsoftJsonSerializer.Instance.Serialize(evt)
        };
    }

    private static Instant? ParseGoogleDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return null;

        if (!string.IsNullOrEmpty(eventDateTime.DateTimeRaw))
            return OffsetDateTimePattern.Rfc3339.Parse(eventDateTime.DateTimeRaw).GetValueOrThrow().ToInstant();

        if (!string.IsNullOrEmpty(eventDateTime.Date))
            return LocalDatePattern.Iso.Parse(eventDateTime.Date).GetValueOrThrow().AtMidnight().ToInstant();

        return null;
    }

    /// <inheritdoc/>
    public Instant? GetEventStartTime(
        string rawEventData,
        Instant? occurrenceTime = null)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
        if (googleEvent == null)
            return null;

        var isRecurring = googleEvent.Recurrence is { Count: > 0 };

        // For non-recurring events or when no occurrence time is specified, return base event start time
        if (!isRecurring || !occurrenceTime.HasValue)
            return ParseGoogleDateTime(googleEvent.Start);

        var occurrence = DetermineOccurrences(
                googleEvent,
                new Interval(occurrenceTime, null),
                max: 1)
            .FirstOrDefault();

        if (occurrence == default)
            // Nothing found?! Well ...
            return ParseGoogleDateTime(googleEvent.Start);

        return occurrence;
    }

    /// <inheritdoc/>
    public IList<int> GetReminderMinutes(
        string rawEventData,
        string? rawCalendarData = null)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
        if (googleEvent?.Reminders == null)
            return [];

        List<int> reminderMinutes = [];

        if (googleEvent.Reminders.UseDefault == true)
        {
            // Use default reminders from calendar
            if (!string.IsNullOrEmpty(rawCalendarData))
            {
                var calendarListEntry =
                    NewtonsoftJsonSerializer.Instance.Deserialize<CalendarListEntry>(rawCalendarData);
                if (calendarListEntry?.DefaultReminders != null)
                {
                    foreach (var reminder in calendarListEntry.DefaultReminders.Where(r =>
                                 r.Method == "popup" && r.Minutes.HasValue))
                    {
                        reminderMinutes.Add(reminder.Minutes!.Value);
                    }
                }
            }
        }
        else
        {
            // Use event-specific reminders
            if (googleEvent.Reminders.Overrides != null)
            {
                foreach (var reminder in googleEvent.Reminders.Overrides.Where(r =>
                             r.Method == "popup" && r.Minutes.HasValue))
                {
                    reminderMinutes.Add(reminder.Minutes!.Value);
                }
            }
        }

        return reminderMinutes;
    }

    /// <inheritdoc/>
    public IList<(Instant Occurrence, Instant TriggerTime, string? TargetEventId)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        Instant referenceTime = default,
        IList<string>? overrides = null)
    {
        try
        {
            var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<GoogleEvent>(rawEventData);
            if (googleEvent == null)
                return [];

            var reminderMinutes = GetReminderMinutes(rawEventData, rawCalendarData);
            if (reminderMinutes.Count == 0)
                return [];

            var eventStartTime = ParseGoogleDateTime(googleEvent.Start);
            if (!eventStartTime.HasValue)
                return [];

            var isRecurring = googleEvent.Recurrence is { Count: > 0 };
            var refTime = referenceTime == default
                ? _clock.GetCurrentInstant()
                : referenceTime;
            var result = new List<(Instant Occurrence, Instant TriggerTime, string? TargetEventId)>();

            if (isRecurring)
            {
                // Parse overrides
                var parsedOverrides = new List<GoogleEvent>();
                if (overrides != null)
                {
                    foreach (var overrideData in overrides)
                    {
                        var overrideEvent = NewtonsoftJsonSerializer.Instance.Deserialize<GoogleEvent>(overrideData);
                        if (overrideEvent != null)
                        {
                            parsedOverrides.Add(overrideEvent);
                        }
                    }
                }

                // Get more occurrences to ensure we find one that is not overridden or we can handle overrides
                var occurrences = DetermineOccurrences(googleEvent, new Interval(refTime, null), max: 5);

                foreach (var occurrence in occurrences)
                {
                    // Check if this occurrence is overridden
                    var overrideEvent = parsedOverrides.FirstOrDefault(o =>
                        ParseGoogleDateTime(o.OriginalStartTime) == occurrence);

                    if (overrideEvent != null)
                    {
                        if (overrideEvent.Status == "cancelled")
                        {
                            continue; // This occurrence is cancelled, skip it
                        }

                        // Use override's start time and reminder settings
                        var overrideStartTime = ParseGoogleDateTime(overrideEvent.Start);
                        if (!overrideStartTime.HasValue) continue;

                        var overrideReminderMinutes = GetReminderMinutes(
                            NewtonsoftJsonSerializer.Instance.Serialize(overrideEvent),
                            rawCalendarData);

                        if (overrideReminderMinutes.Count == 0) continue;

                        foreach (var minutes in overrideReminderMinutes)
                        {
                            var triggerTime = overrideStartTime.Value.Plus(Duration.FromMinutes(-minutes));
                            if (triggerTime >= refTime)
                            {
                                result.Add((overrideStartTime.Value, triggerTime, overrideEvent.Id));
                                return result; // Found the next reminder
                            }
                        }
                    }
                    else
                    {
                        // Use master's occurrence and reminder settings
                        foreach (var minutes in reminderMinutes)
                        {
                            var triggerTime = occurrence.Plus(Duration.FromMinutes(-minutes));
                            if (triggerTime >= refTime)
                            {
                                result.Add((occurrence, triggerTime, null));
                                return result; // Found the next reminder
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var minutes in reminderMinutes)
                {
                    var triggerTime = eventStartTime.Value.Plus(Duration.FromMinutes(-minutes));
                    if (triggerTime > refTime)
                        result.Add((eventStartTime.Value, triggerTime, null));
                }
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static List<Instant> DetermineOccurrences(GoogleEvent evt, Interval timeRange,
        int max = Int32.MaxValue)
    {
        if (evt.Recurrence == null || evt.Recurrence.Count == 0)
            return [];

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"DTSTART;TZID={evt.Start.TimeZone}:{evt.Start.DateTimeDateTimeOffset:yyyyMMdd'T'HHmmss}");

        foreach (var r in evt.Recurrence)
            sb.AppendLine(r);

        sb.AppendLine("END:VEVENT");
        sb.Append("END:VCALENDAR");

        var calendar = Calendar.Load(sb.ToString());
        var icalEvent = calendar?.Events.FirstOrDefault();

        if (icalEvent == null)
            throw new InvalidOperationException("failed to parse recurrence");

        var occurrences = icalEvent.GetOccurrences(
            new CalDateTime(timeRange.Start.ToDateTimeUtc()));

        return occurrences
            .Select(o => Instant.FromDateTimeOffset(o.Period.StartTime.AsUtc))
            .TakeWhile(t => !timeRange.HasEnd || t <= timeRange.End)
            .Take(max)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Respond to the event using the service
        await googleCalendarService.RespondToEventAsync(service, calendarId, eventId, responseStatus,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(string externalId, string rawData)> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        ModelExtensions extensions,
        LocalDateTime startTime,
        LocalDateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        var googleEvent = new GoogleEvent
        {
            Summary = title
        };

        var isFullDay = extensions.Get(CalendarEventExtensions.FullDay);
        if (isFullDay)
        {
            googleEvent.Start = new EventDateTime
            {
                Date = LocalDatePattern.Iso.Format(startTime.Date)
            };
            googleEvent.End = new EventDateTime
            {
                Date = LocalDatePattern.Iso.Format(endTime.Date)
            };
        }
        else
        {
            googleEvent.Start = new EventDateTime
            {
                DateTimeRaw = OffsetDateTimePattern.Rfc3339.Format(startTime.ToZonedDateTime().ToOffsetDateTime()),
                TimeZone = TimeZoneInfo.Local.Id
            };
            googleEvent.End = new EventDateTime
            {
                DateTimeRaw = OffsetDateTimePattern.Rfc3339.Format(endTime.ToZonedDateTime().ToOffsetDateTime()),
                TimeZone = TimeZoneInfo.Local.Id
            };
        }

        var description = extensions.Get(CalendarEventExtensions.Description) switch
        {
            RichText.HTML html => html.value,
            RichText.SimpleText st => st.value,
            _ => null
        };

        if (description != null)
            googleEvent.Description = description;

        var location = extensions.Get(CalendarEventExtensions.Location);
        if (location != null)
            googleEvent.Location = location;

        var externalId =
            await googleCalendarService.CreateEventAsync(service, calendarId, googleEvent, cancellationToken);

        var rawData = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);

        return (externalId, rawData);
    }

    /// <inheritdoc/>
    public async Task<string> UpdateEventAsync(
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken = default)
    {
        var accountId = calendarEvent.Reference.Calendar.Account.Id.ToString();
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {calendarEvent.Reference.Calendar.Account.Name}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        var googleEvent = calendarEvent.Extensions.Get(GoogleEventExtension) ??
                          throw new InvalidOperationException("Event without data");

        var startTime = calendarEvent.StartTime;
        var endTime = calendarEvent.EndTime;

        var isFullDay = calendarEvent.Extensions.Get(CalendarEventExtensions.FullDay);
        if (isFullDay)
        {
            googleEvent.Start = new EventDateTime
            {
                Date = LocalDatePattern.Iso.Format(startTime.Date)
            };
            googleEvent.End = new EventDateTime
            {
                Date = LocalDatePattern.Iso.Format(endTime.Date)
            };
        }
        else
        {
            googleEvent.Start = new EventDateTime
            {
                DateTimeRaw = OffsetDateTimePattern.Rfc3339.Format(startTime.ToZonedDateTime().ToOffsetDateTime()),
                TimeZone = TimeZoneInfo.Local.Id
            };
            googleEvent.End = new EventDateTime
            {
                DateTimeRaw = OffsetDateTimePattern.Rfc3339.Format(endTime.ToZonedDateTime().ToOffsetDateTime()),
                TimeZone = TimeZoneInfo.Local.Id
            };
        }

        var description = calendarEvent.Extensions.Get(CalendarEventExtensions.Description) switch
        {
            RichText.HTML html => html.value,
            RichText.SimpleText st => st.value,
            _ => null
        };

        if (description != null)
            googleEvent.Description = description;

        var location = calendarEvent.Extensions.Get(CalendarEventExtensions.Location);
        if (location != null)
            googleEvent.Location = location;

        var calendarId = calendarEvent.Reference.Calendar.ExternalId ?? throw new InvalidOperationException("Calendar ExternalId is null");
        var eventId = calendarEvent.Reference.ExternalId ?? throw new InvalidOperationException("Event ExternalId is null");

        await googleCalendarService.UpdateEventAsync(service, calendarId, eventId, googleEvent, cancellationToken);

        var rawData = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);

        return rawData;
    }

    /// <inheritdoc/>
    public async Task DeleteEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        await googleCalendarService.DeleteEventAsync(service, calendarId, eventId, cancellationToken);
    }

    /// <inheritdoc/>
    public IList<object> GetSupportedExtensions() =>
    [
        CalendarEventExtensions.FullDay,
        CalendarEventExtensions.TimeZone,
        CalendarEventExtensions.Location,
        CalendarEventExtensions.Description,
        CalendarEventExtensions.Attachments,
        CalendarEventExtensions.Conference,
        CalendarEventExtensions.Participants,
        CalendarEventExtensions.Participation
    ];
}