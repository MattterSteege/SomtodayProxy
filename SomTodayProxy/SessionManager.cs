using System.Collections.Concurrent;

namespace SomtodayProxy
{
    public class SessionManager : IHostedService
    {
        private readonly ConcurrentDictionary<string, UserSession?> _sessions = new();
        private Timer _cleanupTimer;

        public void StartCleanupTimer()
        {
            //this might not be the absolute best way to do this, but it works.
            //a session can range from 5:00 to 5:59, seconds, but not a biggie
            _cleanupTimer = new Timer(CleanupExpiredSessions!, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public UserSession? CreateSession(string user, string callbackUrl)
        {
            if (user == null) return null;
            if (callbackUrl == null) return null;
            
            if (_sessions.Values.Any(s => s.User == user))
            {
                return _sessions.Values.First(s => s.User == user);
            }
            
            var session = new UserSession
            {
                User = user,
                VanityUrl = GenerateUniqueVanityUrl(),
                Expires = DateTime.UtcNow.AddMinutes(Constants.SessionDuration),
                CallbackUrl = callbackUrl
            };

            _sessions.TryAdd(session.VanityUrl, session);
            return session;
        }

        public UserSession GetSession(string vanityUrl)
        {
            _sessions.TryGetValue(vanityUrl, out var session);
            return session;
        }

        public void RemoveSession(string vanityUrl)
        {
            _sessions.TryRemove(vanityUrl, out _);
        }

        private string GenerateUniqueVanityUrl()
        {
            string vanityUrl;
            do
            {
                vanityUrl = Constants.BaseVanitUrl + new Random().Next(0, 9999).ToString("D4");
            } while (_sessions.ContainsKey(vanityUrl));

            return vanityUrl;
        }
        
        public int GetSessionCount()
        {
            return _sessions.Count;
        }

        private void CleanupExpiredSessions(object state)
        {
            var expiredSessions = _sessions.Where(kvp => kvp.Value.Expires < DateTime.UtcNow);
            foreach (var session in expiredSessions)
            {
                _sessions.TryRemove(session.Key, out _);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCleanupTimer();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cleanupTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }
    }

    public class UserSession
    {
        public string User { get; set; }
        public string VanityUrl { get; set; }
        public DateTime Expires { get; set; }
        public string CallbackUrl { get; set; }
    }
}