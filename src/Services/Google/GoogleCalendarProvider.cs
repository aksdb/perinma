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
using perinma.Services;
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
    
    private const string EventStatusCancelled = "cancelled";

    /// <inheritdoc/>
    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, Interval timeRange) =>
        ParseCalendarEventsInternal(rawEvents, timeRange);

    /// <inheritdoc/>
    public CalendarEvent ParseEventForEdit(RawEvent rawEvent)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<GoogleEvent>(rawEvent.RawData)
                          ?? throw new InvalidOperationException("Failed to parse Google event");
        return MapToCalendarEvent(rawEvent.Reference, googleEvent, null);
    }

    private List<CalendarEvent> ParseCalendarEventsInternal(List<RawEvent> rawEvents, Interval timeRange)
    {
        var googleEvents = rawEvents
            .Select(e => (e.Reference, Event: NewtonsoftJsonSerializer.Instance.Deserialize<Event>(e.RawData)))
            .Where(t => t.Event != null)
            .ToList();

        var overrides = googleEvents
            .Where(t => !string.IsNullOrEmpty(t.Event.RecurringEventId))
            .ToList();

        return googleEvents
            .Where(t => t.Event.Status != EventStatusCancelled && string.IsNullOrEmpty(t.Event.RecurringEventId))
            .SelectMany(t =>
            {
                if (t.Event.Recurrence is { Count: > 0 })
                {
                    return DetermineOccurrences(t.Event, timeRange)
                        .Where(occurrenceStart => !overrides.Any(ov =>
                            ov.Event.RecurringEventId == t.Event.Id &&
                            ParseGoogleDateTime(ov.Event.OriginalStartTime) == occurrenceStart))
                        .Select(occurrenceStart => MapToCalendarEvent(t.Reference, t.Event, occurrenceStart));
                }

                return [MapToCalendarEvent(t.Reference, t.Event, null)];
            })
            .Concat(overrides
                .Where(ov => ov.Event.Status != EventStatusCancelled)
                .Select(ov => MapToCalendarEvent(ov.Reference, ov.Event, null)))
            .Where(ce => ce.StartTime.ToInstant() <= timeRange.End && ce.EndTime.ToInstant() >= timeRange.Start)
            .ToList();
    }

    private CalendarEvent MapToCalendarEvent(EventReference reference, GoogleEvent googleEvent,
        Instant? occurrenceStart)
    {
        var start = ParseGoogleDateTime(googleEvent.Start) ?? throw new InvalidOperationException("event without start time");
        var end = ParseGoogleDateTime(googleEvent.End) ?? throw new InvalidOperationException("event without end time");

        if (occurrenceStart.HasValue)
        {
            var duration = end.Minus(start);
            start = occurrenceStart.Value;
            end = start.Plus(duration);
        }

        string? timeZone = null;
        if (!string.IsNullOrEmpty(googleEvent.Start.TimeZone))
            timeZone = googleEvent.Start.TimeZone;
        bool fullDay = !string.IsNullOrEmpty(googleEvent.Start.Date);

        var relevantStatus = googleEvent.Attendees
            ?.FirstOrDefault(a => a.Self == true)
            ?.ResponseStatus;

        var extensions = new ModelExtensions();
        extensions.Set(GoogleEventExtension, googleEvent);
        extensions.Set(CalendarEventExtensions.RecurrenceEdit, BuildRecurrenceEditInfo(googleEvent, occurrenceStart));
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
        if (googleEvent.Transparency == "transparent")
            extensions.Set(CalendarEventExtensions.NonBlocking, true);

        var localStartTime = start.ToLocalDateTime();
        var localEndTime = end.ToLocalDateTime();

        if (fullDay)
        {
            localStartTime = localStartTime.Date.AtMidnight();
            localEndTime = localEndTime.Date.AtMidnight();
        }
        extensions.Set(CalendarEventExtensions.RecurrenceInfo, RecurrenceParser.GetGoogleRecurrenceInfo(googleEvent.Recurrence, localStartTime));

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

    private static RecurrenceEditInfo BuildRecurrenceEditInfo(GoogleEvent googleEvent, Instant? occurrenceStart)
    {
        var actions = new HashSet<RecurringEventAction>();
        var originalStartTime = ParseGoogleDateTime(googleEvent.OriginalStartTime) ?? occurrenceStart;

        if (!string.IsNullOrEmpty(googleEvent.RecurringEventId))
        {
            actions.Add(RecurringEventAction.EditOccurrence);
            actions.Add(RecurringEventAction.EditSeries);
            actions.Add(RecurringEventAction.DeleteOccurrence);
            actions.Add(RecurringEventAction.DeleteSeries);
            return new RecurrenceEditInfo
            {
                Kind = RecurrenceEditKind.OverrideOccurrence,
                SeriesExternalId = googleEvent.RecurringEventId,
                OriginalStartTime = originalStartTime,
                BackingExternalId = googleEvent.Id,
                AllowedActions = actions,
            };
        }

        if (googleEvent.Recurrence is { Count: > 0 })
        {
            actions.Add(RecurringEventAction.EditSeries);
            actions.Add(RecurringEventAction.DeleteSeries);

            if (occurrenceStart.HasValue)
            {
                actions.Add(RecurringEventAction.EditOccurrence);
                actions.Add(RecurringEventAction.DeleteOccurrence);
                return new RecurrenceEditInfo
                {
                    Kind = RecurrenceEditKind.GeneratedOccurrence,
                    SeriesExternalId = googleEvent.Id,
                    OriginalStartTime = occurrenceStart,
                    BackingExternalId = googleEvent.Id,
                    AllowedActions = actions,
                };
            }

            return new RecurrenceEditInfo
            {
                Kind = RecurrenceEditKind.SeriesMaster,
                SeriesExternalId = googleEvent.Id,
                BackingExternalId = googleEvent.Id,
                AllowedActions = actions,
            };
        }

        return new RecurrenceEditInfo
        {
            Kind = RecurrenceEditKind.None,
            BackingExternalId = googleEvent.Id,
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

    private static string? MapResponseStatus(EventResponseStatus status) => status switch
    {
        EventResponseStatus.NeedsAction => "needsAction",
        EventResponseStatus.Declined => "declined",
        EventResponseStatus.Tentative => "tentative",
        EventResponseStatus.Accepted => "accepted",
        _ => null
    };


    /// <inheritdoc/>
    public void EnrichCalendar(perinma.Models.Calendar calendar, Func<string, string?> getData)
    {
        var rawData = getData("rawData");
        if (rawData is null) return;
        try
        {
            var entry = NewtonsoftJsonSerializer.Instance.Deserialize<CalendarListEntry>(rawData);
            // "reader" and "freeBusyReader" are read-only access roles; "owner" and "writer"
            // can create/edit events and are treated as owned calendars that block time.
            var role = entry?.AccessRole;
            if (role is "reader" or "freeBusyReader")
                calendar.Extensions.Set(CalendarExtensions.IsReadOnly, true);
        }
        catch
        {
            // Malformed rawData — leave extension unset (safe default: not read-only).
        }
    }

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
        if (!isOverride && evt.Status == EventStatusCancelled)
        {
            return new ProviderEvent
            {
                ExternalId = evt.Id,
                Title = evt.Summary,
                Status = evt.Status,
                Deleted = true,
                Data = new Dictionary<string, DataAttribute>
                {
                    { "rawData", new DataAttribute.JsonText(NewtonsoftJsonSerializer.Instance.Serialize(evt)) }, 
                }
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

            if (evt.Status == EventStatusCancelled)
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
            Data = new Dictionary<string, DataAttribute>
            {
                { "rawData", new DataAttribute.JsonText(NewtonsoftJsonSerializer.Instance.Serialize(evt)) }, 
            }
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
                        if (overrideEvent.Status == EventStatusCancelled)
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
        SendInvitesResult sendUpdates = SendInvitesResult.SendToAll,
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

        var recurrenceInfo = extensions.Get(CalendarEventExtensions.RecurrenceInfo);
        if (recurrenceInfo is { IsRecurring: true, Rule: not null })
            googleEvent.Recurrence = [RecurrenceParser.BuildGoogleRecurrence(recurrenceInfo.Rule, startTime)];

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

        // Handle reminder
        var reminderMinutes = extensions.Get(CalendarEventExtensions.ReminderMinutesBefore);
        if (reminderMinutes >= 0)
        {
            googleEvent.Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new List<EventReminder>
                {
                    new() { Method = "popup", Minutes = reminderMinutes }
                }
            };
        }

        // Handle participants
        var participants = extensions.Get(CalendarEventExtensions.Participants);
        if (participants != null && participants.Count > 0)
        {
            googleEvent.Attendees = participants.Select(p => new global::Google.Apis.Calendar.v3.Data.EventAttendee
            {
                Email = p.Email,
                DisplayName = p.Name,
                Optional = p.IsOptional,
                ResponseStatus = MapResponseStatus(p.Status)
            }).ToList();
        }

        var externalId =
            await googleCalendarService.CreateEventAsync(service, calendarId, googleEvent, sendUpdates, cancellationToken);

        var rawData = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);

        return (externalId, rawData);
    }

    /// <inheritdoc/>
    public async Task UpdateEventAsync(
        CalendarEvent calendarEvent,
        EventEditScope scope,
        SendInvitesResult sendUpdates = SendInvitesResult.SendToAll,
        CancellationToken cancellationToken = default)
    {
        var accountId = calendarEvent.Reference.Calendar.Account.Id.ToString();
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {calendarEvent.Reference.Calendar.Account.Name}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var calendarId = calendarEvent.Reference.Calendar.ExternalId
            ?? throw new InvalidOperationException("Calendar ExternalId is null");
        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };

        var googleEvent = await ResolveGoogleEventForUpdateAsync(service, calendarId, calendarEvent, scope, recurrenceEdit,
            cancellationToken);
        ApplyEditableValues(calendarEvent, googleEvent, scope != EventEditScope.Occurrence);

        await googleCalendarService.UpdateEventAsync(service, calendarId, googleEvent.Id, googleEvent, sendUpdates,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteEventAsync(
        CalendarEvent calendarEvent,
        EventDeleteAction action,
        CancellationToken cancellationToken = default)
    {
        var accountId = calendarEvent.Reference.Calendar.Account.Id.ToString();
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var calendarId = calendarEvent.Reference.Calendar.ExternalId
            ?? throw new InvalidOperationException("Calendar ExternalId is null");
        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };

        switch (action)
        {
            case EventDeleteAction.Event:
                await googleCalendarService.DeleteEventAsync(service, calendarId,
                    calendarEvent.Reference.ExternalId ?? throw new InvalidOperationException("Event ExternalId is null"),
                    cancellationToken);
                return;
            case EventDeleteAction.Series:
                await googleCalendarService.DeleteEventAsync(service, calendarId,
                    recurrenceEdit.SeriesExternalId ?? calendarEvent.Reference.ExternalId
                    ?? throw new InvalidOperationException("Series ExternalId is null"),
                    cancellationToken);
                return;
            case EventDeleteAction.Occurrence:
            {
                var occurrence = await ResolveGoogleOccurrenceAsync(service, calendarId, calendarEvent, recurrenceEdit,
                    cancellationToken);
                occurrence.Status = EventStatusCancelled;
                await googleCalendarService.UpdateEventAsync(service, calendarId, occurrence.Id, occurrence,
                    SendInvitesResult.SendToNone, cancellationToken);
                return;
            }
            case EventDeleteAction.RevertOverride:
                throw new InvalidOperationException("Google Calendar does not support reverting overrides safely.");
            default:
                throw new InvalidOperationException($"Unsupported delete action {action}");
        }
    }

    private async Task<GoogleEvent> ResolveGoogleEventForUpdateAsync(
        global::Google.Apis.Calendar.v3.CalendarService service,
        string calendarId,
        CalendarEvent calendarEvent,
        EventEditScope scope,
        RecurrenceEditInfo recurrenceEdit,
        CancellationToken cancellationToken)
    {
        return scope switch
        {
            EventEditScope.Event => calendarEvent.Extensions.Get(GoogleEventExtension)
                                    ?? throw new InvalidOperationException("Event without data"),
            EventEditScope.Series => await googleCalendarService.GetEventAsync(
                service,
                calendarId,
                recurrenceEdit.SeriesExternalId ?? calendarEvent.Reference.ExternalId
                ?? throw new InvalidOperationException("Series ExternalId is null"),
                cancellationToken),
            EventEditScope.Occurrence => await ResolveGoogleOccurrenceAsync(service, calendarId, calendarEvent,
                recurrenceEdit, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported edit scope {scope}")
        };
    }

    private async Task<GoogleEvent> ResolveGoogleOccurrenceAsync(
        global::Google.Apis.Calendar.v3.CalendarService service,
        string calendarId,
        CalendarEvent calendarEvent,
        RecurrenceEditInfo recurrenceEdit,
        CancellationToken cancellationToken)
    {
        if (recurrenceEdit.Kind == RecurrenceEditKind.OverrideOccurrence)
        {
            var overrideId = recurrenceEdit.BackingExternalId ?? calendarEvent.Reference.ExternalId
                ?? throw new InvalidOperationException("Override event id is missing");
            return await googleCalendarService.GetEventAsync(service, calendarId, overrideId, cancellationToken);
        }

        if (recurrenceEdit.Kind != RecurrenceEditKind.GeneratedOccurrence || !recurrenceEdit.OriginalStartTime.HasValue)
            throw new InvalidOperationException("This event does not identify a single occurrence");

        var occurrence = await googleCalendarService.GetOccurrenceAsync(service, calendarId,
            recurrenceEdit.SeriesExternalId ?? throw new InvalidOperationException("Series ExternalId is null"),
            recurrenceEdit.OriginalStartTime.Value, cancellationToken);

        return occurrence ?? throw new InvalidOperationException("Could not resolve recurring occurrence");
    }

    private static void ApplyEditableValues(CalendarEvent calendarEvent, GoogleEvent googleEvent, bool applyRecurrence)
    {
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

        googleEvent.Summary = calendarEvent.Title;

        var description = calendarEvent.Extensions.Get(CalendarEventExtensions.Description) switch
        {
            RichText.HTML html => html.value,
            RichText.SimpleText st => st.value,
            _ => null
        };

        googleEvent.Description = description;
        googleEvent.Location = calendarEvent.Extensions.Get(CalendarEventExtensions.Location);

        var reminderMinutes = calendarEvent.Extensions.Get(CalendarEventExtensions.ReminderMinutesBefore);
        if (reminderMinutes >= 0)
        {
            googleEvent.Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new List<EventReminder>
                {
                    new() { Method = "popup", Minutes = reminderMinutes }
                }
            };
        }
        else
        {
            googleEvent.Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = []
            };
        }

        var participants = calendarEvent.Extensions.Get(CalendarEventExtensions.Participants);
        googleEvent.Attendees = participants?.Count > 0
            ? participants.Select(p => new global::Google.Apis.Calendar.v3.Data.EventAttendee
            {
                Email = p.Email,
                DisplayName = p.Name,
                Optional = p.IsOptional,
                ResponseStatus = MapResponseStatus(p.Status)
            }).ToList()
            : [];

        if (applyRecurrence)
        {
            var recurrenceInfo = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceInfo);
            if (recurrenceInfo is { IsRecurring: true, Rule: not null })
            {
                var preservedEntries = googleEvent.Recurrence?
                    .Where(value => !value.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? [];
                googleEvent.Recurrence = [RecurrenceParser.BuildGoogleRecurrence(recurrenceInfo.Rule, startTime), .. preservedEntries];
            }
            else if (recurrenceInfo is { IsRecurring: false })
            {
                googleEvent.Recurrence = null;
            }
        }
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
        CalendarEventExtensions.Participation,
        CalendarEventExtensions.RecurrenceInfo
    ];

    /// <inheritdoc/>
    public async Task<IList<AttendeeFreeBusy>> GetFreeBusyAsync(
        string accountId,
        IList<string> attendeeEmails,
        Interval timeRange,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
            return attendeeEmails.Select(e => new AttendeeFreeBusy
            {
                Email = e,
                Status = FreeBusyStatus.Unknown
            }).ToList<AttendeeFreeBusy>();

        var service = await googleCalendarService.CreateServiceAsync(
            googleCredentials, cancellationToken, accountId);

        var request = new FreeBusyRequest
        {
            TimeMinDateTimeOffset = timeRange.Start.ToDateTimeOffset(),
            TimeMaxDateTimeOffset = timeRange.End.ToDateTimeOffset(),
            Items = attendeeEmails
                .Select(e => new FreeBusyRequestItem { Id = e })
                .ToList()
        };

        var response = await googleCalendarService.GetFreeBusyAsync(
            service, request, cancellationToken);

        return attendeeEmails.Select(email =>
        {
            if (response.Calendars == null ||
                !response.Calendars.TryGetValue(email, out var cal))
                return new AttendeeFreeBusy
                {
                    Email = email,
                    Status = FreeBusyStatus.Unknown
                };

            if (cal.Errors is { Count: > 0 })
                return new AttendeeFreeBusy
                {
                    Email = email,
                    Status = FreeBusyStatus.Unavailable
                };

            var slots = (cal.Busy ?? [])
                .Where(p => p.StartDateTimeOffset.HasValue && p.EndDateTimeOffset.HasValue)
                .Select(p => new TimeSlot(
                    Instant.FromDateTimeOffset(p.StartDateTimeOffset!.Value),
                    Instant.FromDateTimeOffset(p.EndDateTimeOffset!.Value)))
                .OrderBy(s => s.Start)
                .ToList();

            return new AttendeeFreeBusy
            {
                Email = email,
                Status = FreeBusyStatus.Ok,
                BusySlots = slots
            };
        }).ToList<AttendeeFreeBusy>();
    }
}