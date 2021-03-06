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
    public class ErrorLogsController : Controller
    {
        private readonly TwitchBotDbContext _context;

        public ErrorLogsController(TwitchBotDbContext context)
        {
            _context = context;
        }

        // POST: api/errorlogs/create
        /* Body (JSON): 
            { 
              "errorTime": "2018-01-01T15:30:00",
              "errorLine": 9001,
              "errorClass": "SomeClass",
              "errorMethod": "SomeMethod",
              "errorMsg": "Some Error Message",
              "broadcaster": 2,
              "command": "!somecmd",
              "userMsg": "n/a"
            }
        */
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ErrorLog errorLog)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _context.ErrorLog.Add(errorLog);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}