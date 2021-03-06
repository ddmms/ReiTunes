﻿using Microsoft.Data.Sqlite;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReiTunes.Core {

    public class Library {

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler LibraryItemsRebuilt;

        public string MachineName { get; private set; }

        public List<LibraryItem> Items { get; set; } = new List<LibraryItem>();
        private readonly ISerializedEventRepository _repo;
        private readonly ServerCaller _caller;
        private readonly ILogger _logger;
        private readonly LibraryItemEventFactory _eventFactory;

        public Library(SqliteConnection connection, ServerCaller caller, ILogger logger)
            : this(Environment.MachineName, connection, caller, logger, new Clock()) { }

        public Library(string machineName, SqliteConnection connection, ServerCaller caller, ILogger logger)
            : this(machineName, connection, caller, logger, new Clock()) { }

        public Library(string machineName, SqliteConnection connection, ServerCaller caller, ILogger logger, IClock clock) {
            MachineName = machineName;
            _caller = caller;
            _logger = logger;
            _repo = new SQLiteEventRepository(connection, logger);
            _eventFactory = new LibraryItemEventFactory(MachineName, clock);
            RebuildItems();
        }

        public void ReceiveEvents(IEnumerable<IEvent> events) {
            foreach (var @event in events) {
                _repo.Save(@event);
            }
            RebuildItems();
        }

        public void ReceiveEvent(IEvent @event) {
            ReceiveEvents(new List<IEvent> { @event });
        }

        //public IEnumerable<IEvent> GetAllEvents() {
        //    var sw = Stopwatch.StartNew();
        //    var ret = _repo.GetAllEvents();
        //    sw.Stop();
        //    _logger.Information("GetAllEvents took {ElapsedMs}", sw.ElapsedMilliseconds);
        //    return ret;
        //}

        public async Task PullFromServer() {
            var events = await _caller.PullAllEventsAsync();
            ReceiveEvents(events);
        }

        public async Task PushToServer() {
            var eventsToPush = _repo.GetAllEventsFromMachine(MachineName);

            await _caller.PushEventsAsync(eventsToPush);

            var pushedCount = eventsToPush.Count();
            var totalEventCount = (double)_repo.CountOfAllEvents();

            _logger.Information("Pushed {PushedCount} of {TotalEventCount} events ({PercentageOfAllEvents}%)",
                pushedCount, totalEventCount, 100 * pushedCount / totalEventCount);
        }

        private void RebuildItems() {
            var sw = Stopwatch.StartNew();
            Items.Clear();

            var events = _repo.GetAllEvents();

            var groupedEvents = events.GroupBy(e => e.AggregateId).Select(g => g.OrderBy(e => e.CreatedTimeUtc).ThenBy(e => e.LocalId));

            foreach (var aggregateEvents in groupedEvents) {
                var aggregate = new LibraryItem(_eventFactory);

                var first = aggregateEvents.First();

                if (!(first is LibraryItemCreatedEvent)) {
                    throw new Exception($"Bad event data: first event for item {first.AggregateId} is of type {first.GetType()} not {nameof(LibraryItemCreatedEvent)}");
                }

                aggregate.EventCreated += SaveEventToRepo;
                foreach (var @event in aggregateEvents) {
                    aggregate.Apply(@event);
                }
                Items.Add(aggregate);
            }

            sw.Stop();
            _logger.Information("Rebuilding all items took {ElapsedMs}", sw.ElapsedMilliseconds);

            LibraryItemsRebuilt?.Invoke(this, EventArgs.Empty);
        }

        private void SaveEventToRepo(object sender, IEvent e) {
            _repo.Save(e);
            var agg = (Aggregate)sender;
            agg.Commit();
        }
    }
}