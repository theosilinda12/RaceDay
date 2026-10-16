using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    public EventsController(RaceDayDbContext db) => _db = db;

    /// <summary>Lists all events. Optional filters: date (that day), location (contains), eventType.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<EventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<EventDto>>> GetEvents(
        [FromQuery] DateTime? date, [FromQuery] string? location, [FromQuery] string? eventType)
    {
        var q = _db.Events.AsNoTracking().AsQueryable();

        if (date.HasValue)
        {
            var start = date.Value.Date;
            var end = start.AddDays(1);
            q = q.Where(e => e.EventDate >= start && e.EventDate < end);
        }
        if (!string.IsNullOrWhiteSpace(location))
            q = q.Where(e => e.Location.Contains(location));
        if (!string.IsNullOrWhiteSpace(eventType))
            q = q.Where(e => e.EventType == eventType);

        var events = await q.OrderBy(e => e.EventDate).ToListAsync();
        return Ok(events.Select(e => e.ToDto()));
    }

    /// <summary>Returns one event with its categories.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailDto>> GetById(int id)
    {
        var ev = await _db.Events.AsNoTracking().Include(e => e.Categories)
            .FirstOrDefaultAsync(e => e.EventId == id);
        return ev is null ? NotFound() : Ok(ev.ToDetailDto());
    }

    /// <summary>Creates an event owned by the logged-in Organiser.</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Organiser)]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EventDto>> Create(EventRequest req)
    {
        var ev = new Event
        {
            Name = req.Name.Trim(),
            Description = req.Description,
            EventDate = req.EventDate!.Value,
            Location = req.Location.Trim(),
            Distance = req.Distance,
            EventType = req.EventType,
            OrganiserId = User.GetUserId()
        };
        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = ev.EventId }, ev.ToDto());
    }

    /// <summary>Updates an event. Only the owning Organiser may do this.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Organiser)]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDto>> Update(int id, EventRequest req)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.EventId == id);
        if (ev is null) return NotFound();
        if (ev.OrganiserId != User.GetUserId()) return Forbid();

        ev.Name = req.Name.Trim();
        ev.Description = req.Description;
        ev.EventDate = req.EventDate!.Value;
        ev.Location = req.Location.Trim();
        ev.Distance = req.Distance;
        ev.EventType = req.EventType;
        await _db.SaveChangesAsync();
        return Ok(ev.ToDto());
    }

    /// <summary>Deletes an event, its categories, enrolments and results. Only the owning Organiser.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Organiser)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.EventId == id);
        if (ev is null) return NotFound();
        if (ev.OrganiserId != User.GetUserId()) return Forbid();

        // Enrolment -> Event is not cascading in the schema, so remove enrolments (and their results) first
        var enrolments = await _db.Enrolments.Where(e => e.EventId == id).ToListAsync();
        var enrolmentIds = enrolments.Select(e => e.EnrolmentId).ToList();
        var results = await _db.Results.Where(r => enrolmentIds.Contains(r.EnrolmentId)).ToListAsync();

        _db.Results.RemoveRange(results);
        _db.Enrolments.RemoveRange(enrolments);
        _db.Events.Remove(ev);          // categories cascade in the database
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
