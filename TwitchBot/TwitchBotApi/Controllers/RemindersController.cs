﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TwitchBotDb.Models;

namespace TwitchBotApi.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]/[action]")]
    public class RemindersController : Controller
    {
        private readonly TwitchBotDbContext _context;

        public RemindersController(TwitchBotDbContext context)
        {
            _context = context;
        }

        // GET: api/reminders/get/5
        // GET: api/reminders/get/5?id=1
        [HttpGet("{broadcasterId:int}")]
        public async Task<IActionResult> Get([FromRoute] int broadcasterId, [FromQuery] int id = 0)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var reminders = new object();

            if (id == 0)
                reminders = await _context.Reminders.Where(m => m.Broadcaster == broadcasterId).ToListAsync();
            else
                reminders = await _context.Reminders.SingleOrDefaultAsync(m => m.Broadcaster == broadcasterId && m.Id == id);

            if (reminders == null)
            {
                return NotFound();
            }

            return Ok(reminders);
        }

        // PUT: api/reminders/update/5?broadcasterId=2
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update([FromRoute] int id, [FromQuery] int broadcasterId, [FromBody] Reminders reminder)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != reminder.Id && broadcasterId != reminder.Broadcaster)
            {
                return BadRequest();
            }

            _context.Entry(reminder).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RemindersExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/reminders/create
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Reminders reminder)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _context.Reminders.Add(reminder);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/reminders/delete/5?broadcasterId=2
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete([FromRoute] int id, [FromQuery] int broadcasterId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Reminders reminder = await _context.Reminders.SingleOrDefaultAsync(m => m.Id == id && m.Broadcaster == broadcasterId);
            if (reminder == null)
            {
                return NotFound();
            }

            _context.Reminders.Remove(reminder);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool RemindersExists(int id)
        {
            return _context.Reminders.Any(e => e.Id == id);
        }
    }
}