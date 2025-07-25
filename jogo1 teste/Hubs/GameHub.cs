using BreakoutOnline.DataAccess;
using BreakoutOnline.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Timers;

namespace BreakoutOnline.Hubs
{
    public static class GameConstants
    {
        public const int CANVAS_WIDTH = 600;
        public const int CANVAS_HEIGHT = 450;
        public const double PADDLE_NORMAL_WIDTH = 80;
        public const double PADDLE_WIDE_WIDTH = 120;
        public const double PADDLE_SHRUNK_WIDTH = 40;
        public const int PADDLE_HEIGHT = 10;
        public const double PADDLE_SPEED = 8;
        public const double DASH_MULTIPLIER = 3.5;
        public const double DASH_COST = 15;
        public const double ENERGY_REGEN = 0.8;
        public const double BALL_RADIUS = 7;
        public const double BALL_NORMAL_SPEED_X = 4;
        public const double BALL_NORMAL_SPEED_Y = 5;
        public const double BALL_FAST_MULTIPLIER = 1.5;
        public const int RALLIES_TO_SPAWN_POWERUP = 3;
        public const double POWERUP_LIFESPAN_SECONDS = 8.0;
        public const double BALL_STARTING_SPEED_MULTIPLIER = 0.60;
        public const double BALL_MAX_SPEED_MULTIPLIER = 1.25;
        public const double BALL_SECONDS_TO_MAX_SPEED = 12.0;
    }

    public enum GameMode { Standard, SurvivalMarathon }
    public enum PlayerRole { Unassigned, Player1, Player2, Spectator }
    public enum RoomState { Lobby, InGame, PostGame }
    public enum PowerUpType { WidePaddle, FastBall, InvisibleBall, ShrinkOpponentPaddle, TeleportBall }

    public class Player
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Nickname { get; set; } = "Guest";
        public PlayerRole Role { get; set; } = PlayerRole.Unassigned;
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsAI { get; set; } = false;
        public bool HasReturnedToLobby { get; set; } = true;
        public int AIDifficulty { get; set; } = 0;
    }

    public class Paddle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Energy { get; set; } = 100;
        public bool IsDashing { get; set; }
        public DateTime WidePaddleEndTime { get; set; } = DateTime.MinValue;
        public DateTime ShrunkPaddleEndTime { get; set; } = DateTime.MinValue;
    }

    public class Ball
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Radius { get; set; }
        public double Dx { get; set; }
        public double Dy { get; set; }
        public bool IsInvisible { get; set; }
        public bool IsFast { get; set; }
        public DateTime FastBallEndTime { get; set; } = DateTime.MinValue;
        public DateTime InvisibleEndTime { get; set; } = DateTime.MinValue;
    }

    public class PowerUp
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public double X { get; set; }
        public double Y { get; set; }
        public PowerUpType Type { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class GameState
    {
        public ConcurrentDictionary<int, Paddle> Paddles { get; set; } = new();
        public Ball Ball { get; set; } = new();
        public ConcurrentDictionary<int, int> Score { get; set; } = new();
        public double TimeRemaining { get; set; }
        public List<PowerUp> ActivePowerUps { get; set; } = new();
        public int Countdown { get; set; }
        public bool IsPaused { get; set; }
        public int PlayerLives { get; set; } = 0;
    }

    public class Room
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Nova Sala";
        public bool IsPublic { get; set; } = true;
        public string? Password { get; set; }
        public string HostConnectionId { get; set; } = string.Empty;
        public ConcurrentDictionary<string, Player> Players { get; set; } = new();
        public GameState Game { get; set; } = new();
        public int ScoreLimit { get; set; } = 5;
        public int TimeLimit { get; set; } = 180;
        public RoomState State { get; set; } = RoomState.Lobby;
        public int AIDifficulty { get; set; } = 2;
        public GameMode Mode { get; set; } = GameMode.Standard;
    }

    public class InputData
    {
        public double Dx { get; set; }
        public bool IsDashing { get; set; }
    }

    public class AIProfile
    {
        public double SpeedMultiplier { get; set; }
        public double ReactionTimeMs { get; set; }
        public double ErrorMarginPx { get; set; }
    }

    public class AIState
    {
        public double TargetX { get; set; }
        public DateTime LastReactionUpdate { get; set; } = DateTime.UtcNow;
    }

    public class GameLoopManager
    {
        private System.Timers.Timer _timer;
        private readonly Room _room;
        private readonly IHubContext<GameHub> _hubContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, InputData> _playerInputs = new();
        private int _rallyCount = 0;
        private double _nextBallDx, _nextBallDy;
        private DateTime _lastCountdownTick = DateTime.MinValue;
        private double _currentBallSpeed;
        private readonly double _baseBallSpeed;
        private readonly double _maxBallSpeed;
        private readonly double _ballAcceleration;
        private double _elapsedTime = 0;
        private static readonly Dictionary<int, AIProfile> _aiProfiles = new()
        {
            { 1, new AIProfile { SpeedMultiplier = 0.6, ReactionTimeMs = 150, ErrorMarginPx = 35 } },
            { 2, new AIProfile { SpeedMultiplier = 0.9, ReactionTimeMs = 80, ErrorMarginPx = 10 } },
            { 3, new AIProfile { SpeedMultiplier = 1.15, ReactionTimeMs = 25, ErrorMarginPx = 2 } }
        };

        private readonly Dictionary<string, AIState> _aiStates = new();

        public GameLoopManager(Room room, IHubContext<GameHub> hubContext, IServiceProvider serviceProvider)
        {
            _room = room;
            _hubContext = hubContext;
            _serviceProvider = serviceProvider;
            _room.Game.TimeRemaining = _room.TimeLimit;
            _timer = new System.Timers.Timer(1000.0 / 60.0);
            _timer.Elapsed += GameTick;
            _baseBallSpeed = Math.Sqrt(Math.Pow(GameConstants.BALL_NORMAL_SPEED_X, 2) + Math.Pow(GameConstants.BALL_NORMAL_SPEED_Y, 2));
            _maxBallSpeed = _baseBallSpeed * GameConstants.BALL_MAX_SPEED_MULTIPLIER;
            double speedRange = _maxBallSpeed - (_baseBallSpeed * GameConstants.BALL_STARTING_SPEED_MULTIPLIER);
            _ballAcceleration = (speedRange / GameConstants.BALL_SECONDS_TO_MAX_SPEED) * (_timer.Interval / 1000.0);

            foreach (var player in _room.Players.Values.Where(p => p.IsAI))
            {
                _aiStates[player.ConnectionId] = new AIState { TargetX = GameConstants.CANVAS_WIDTH / 2 };
            }
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void UpdatePlayerInput(string connectionId, InputData input) => _playerInputs[connectionId] = input;

        public void StartMatchInitialization()
        {
            ResetBallVelocity(1, true);
            var ball = _room.Game.Ball;
            _room.Game.Countdown = 3;
            _lastCountdownTick = DateTime.MinValue;
            ball.Dx = 0;
            ball.Dy = 0;
            foreach (var state in _aiStates.Values)
            {
                state.TargetX = GameConstants.CANVAS_WIDTH / 2;
            }
        }

        private async void GameTick(object? sender, ElapsedEventArgs e)
        {
            if (_room.State != RoomState.InGame || _room.Game.IsPaused) return;
            if (_room.Game.Countdown > 0)
            {
                if (_lastCountdownTick == DateTime.MinValue) _lastCountdownTick = DateTime.UtcNow;
                if ((DateTime.UtcNow - _lastCountdownTick).TotalSeconds >= 1)
                {
                    _room.Game.Countdown--;
                    _lastCountdownTick = DateTime.UtcNow;
                    if (_room.Game.Countdown == 0)
                    {
                        _room.Game.Ball.Dx = _nextBallDx;
                        _room.Game.Ball.Dy = _nextBallDy;
                    }
                }
                await _hubContext.Clients.Group(_room.Id).SendAsync("GameStateUpdate", _room.Game);
                return;
            }

            if (_room.Mode == GameMode.SurvivalMarathon)
            {
                _elapsedTime += _timer.Interval / 1000.0;
                _room.Game.TimeRemaining = _elapsedTime;
            }
            else
            {
                _room.Game.TimeRemaining -= _timer.Interval / 1000.0;
            }

            if (_currentBallSpeed < _maxBallSpeed)
            {
                _currentBallSpeed += _ballAcceleration;
            }

            UpdatePaddles();
            UpdateAI();
            UpdateBall();
            UpdatePowerUps();
            await CheckForWinConditions();
            await _hubContext.Clients.Group(_room.Id).SendAsync("GameStateUpdate", _room.Game);
        }

        private void UpdatePaddles()
        {
            foreach (var player in _room.Players.Values.Where(p => p.Role == PlayerRole.Player1 || p.Role == PlayerRole.Player2))
            {
                if (!_room.Game.Paddles.TryGetValue((int)player.Role, out var paddle)) continue;

                if (paddle.WidePaddleEndTime < DateTime.UtcNow && paddle.ShrunkPaddleEndTime < DateTime.UtcNow)
                {
                    paddle.Width = GameConstants.PADDLE_NORMAL_WIDTH;
                }

                if (!player.IsAI)
                {
                    paddle.IsDashing = false;
                    if (paddle.Energy < 100) paddle.Energy += GameConstants.ENERGY_REGEN;

                    if (_playerInputs.TryGetValue(player.ConnectionId, out var input))
                    {
                        double currentSpeed = GameConstants.PADDLE_SPEED;
                        if (input.IsDashing && paddle.Energy >= GameConstants.DASH_COST)
                        {
                            currentSpeed *= GameConstants.DASH_MULTIPLIER;
                            paddle.Energy -= GameConstants.DASH_COST;
                            paddle.IsDashing = true;
                        }
                        paddle.X += input.Dx * currentSpeed;
                        paddle.X = Math.Clamp(paddle.X, 0, GameConstants.CANVAS_WIDTH - paddle.Width);
                    }
                }
            }
        }

        private void UpdateAI()
        {
            var aiPlayers = _room.Players.Values.Where(p => p.IsAI && (p.Role == PlayerRole.Player1 || p.Role == PlayerRole.Player2));

            foreach (var aiPlayer in aiPlayers)
            {
                if (!_room.Game.Paddles.TryGetValue((int)aiPlayer.Role, out var aiPaddle) || !_aiStates.TryGetValue(aiPlayer.ConnectionId, out var aiState))
                {
                    continue;
                }

                if (!_aiProfiles.TryGetValue(aiPlayer.AIDifficulty, out var profile))
                {
                    profile = _aiProfiles[2];
                }

                if ((DateTime.UtcNow - aiState.LastReactionUpdate).TotalMilliseconds >= profile.ReactionTimeMs)
                {
                    var randomError = (new Random().NextDouble() * 2 - 1) * profile.ErrorMarginPx;
                    aiState.TargetX = _room.Game.Ball.X + randomError;
                    aiState.LastReactionUpdate = DateTime.UtcNow;
                }

                double targetXWithPaddleOffset = aiState.TargetX - (aiPaddle.Width / 2);
                double aiSpeed = GameConstants.PADDLE_SPEED * profile.SpeedMultiplier;

                if (Math.Abs(aiPaddle.X - targetXWithPaddleOffset) > aiSpeed)
                {
                    aiPaddle.X += (targetXWithPaddleOffset > aiPaddle.X) ? aiSpeed : -aiSpeed;
                }
                else
                {
                    aiPaddle.X = targetXWithPaddleOffset;
                }

                aiPaddle.X = Math.Clamp(aiPaddle.X, 0, GameConstants.CANVAS_WIDTH - aiPaddle.Width);
            }
        }

        private void UpdateBall()
        {
            var ball = _room.Game.Ball;
            ball.IsFast = ball.FastBallEndTime > DateTime.UtcNow;
            double speed = ball.IsFast ? _currentBallSpeed * GameConstants.BALL_FAST_MULTIPLIER : _currentBallSpeed;

            double currentDirectionLength = Math.Sqrt(ball.Dx * ball.Dx + ball.Dy * ball.Dy);
            if (currentDirectionLength > 0)
            {
                ball.Dx = (ball.Dx / currentDirectionLength) * speed;
                ball.Dy = (ball.Dy / currentDirectionLength) * speed;
            }

            if (ball.InvisibleEndTime < DateTime.UtcNow) ball.IsInvisible = false;

            ball.X += ball.Dx;
            ball.Y += ball.Dy;

            if ((ball.X - GameConstants.BALL_RADIUS < 0 && ball.Dx < 0) || (ball.X + GameConstants.BALL_RADIUS > GameConstants.CANVAS_WIDTH && ball.Dx > 0)) ball.Dx *= -1;

            var p1 = _room.Game.Paddles.GetValueOrDefault(1);
            var p2 = _room.Game.Paddles.GetValueOrDefault(2);

            if (p1 != null && ball.Dy > 0 && ball.Y + GameConstants.BALL_RADIUS > p1.Y && ball.Y - GameConstants.BALL_RADIUS < p1.Y + GameConstants.PADDLE_HEIGHT && ball.X + GameConstants.BALL_RADIUS > p1.X && ball.X - GameConstants.BALL_RADIUS < p1.X + p1.Width)
            {
                HandlePaddleCollision(p1, ball);
            }
            if (p2 != null && ball.Dy < 0 && ball.Y - GameConstants.BALL_RADIUS < p2.Y + GameConstants.PADDLE_HEIGHT && ball.Y + GameConstants.BALL_RADIUS > p2.Y && ball.X + GameConstants.BALL_RADIUS > p2.X && ball.X - GameConstants.BALL_RADIUS < p2.X + p2.Width)
            {
                HandlePaddleCollision(p2, ball);
            }
        }

        private void HandlePaddleCollision(Paddle paddle, Ball ball)
        {
            _rallyCount++;
            if (ball.Dy > 0)
            {
                ball.Y = paddle.Y - GameConstants.BALL_RADIUS;
            }
            else
            {
                ball.Y = paddle.Y + GameConstants.PADDLE_HEIGHT + GameConstants.BALL_RADIUS;
            }

            double totalSpeed = _currentBallSpeed;
            if (ball.IsFast) totalSpeed *= GameConstants.BALL_FAST_MULTIPLIER;

            double relativeIntersectX = (paddle.X + (paddle.Width / 2)) - ball.X;
            double normalizedIntersectX = relativeIntersectX / (paddle.Width / 2);
            double bounceAngle = normalizedIntersectX * (Math.PI / 3);

            ball.Dx = totalSpeed * -Math.Sin(bounceAngle);
            ball.Dy = totalSpeed * Math.Cos(bounceAngle);

            if (ball.Y > GameConstants.CANVAS_HEIGHT / 2) ball.Dy = -ball.Dy;
        }

        private void UpdatePowerUps()
        {
            _room.Game.ActivePowerUps.RemoveAll(p => (DateTime.UtcNow - p.CreatedAt).TotalSeconds > GameConstants.POWERUP_LIFESPAN_SECONDS);

            if (_rallyCount >= GameConstants.RALLIES_TO_SPAWN_POWERUP)
            {
                SpawnPowerUp();
                _rallyCount = 0;
            }
            var ball = _room.Game.Ball;
            var playerWhoHitLast = ball.Dy > 0 ? 2 : 1;
            PowerUp? collectedPowerUp = null;
            foreach (var powerUp in _room.Game.ActivePowerUps)
            {
                if (Math.Pow(ball.X - powerUp.X, 2) + Math.Pow(ball.Y - powerUp.Y, 2) < Math.Pow(GameConstants.BALL_RADIUS + 15, 2))
                {
                    ActivatePowerUp(powerUp, playerWhoHitLast);
                    collectedPowerUp = powerUp;
                    break;
                }
            }
            if (collectedPowerUp != null) _room.Game.ActivePowerUps.Remove(collectedPowerUp);
        }

        private async Task CheckForWinConditions()
        {
            var ball = _room.Game.Ball;
            bool pointScored = false;
            int scoringPlayer = 0;

            if (ball.Y + GameConstants.BALL_RADIUS > GameConstants.CANVAS_HEIGHT + 10) { pointScored = true; scoringPlayer = 2; }
            else if (ball.Y - GameConstants.BALL_RADIUS < -10) { pointScored = true; scoringPlayer = 1; }

            if (pointScored)
            {
                if (_room.Mode == GameMode.SurvivalMarathon)
                {
                    _room.Game.PlayerLives--;
                    if (_room.Game.PlayerLives <= 0)
                    {
                        await EndMatch(null, _elapsedTime);
                        return;
                    }
                }
                else
                {
                    _room.Game.Score[scoringPlayer]++;
                    var score1 = _room.Game.Score.GetValueOrDefault(1);
                    var score2 = _room.Game.Score.GetValueOrDefault(2);
                    if (score1 >= _room.ScoreLimit) { await EndMatch(GetPlayerNickname(1)); return; }
                    if (score2 >= _room.ScoreLimit) { await EndMatch(GetPlayerNickname(2)); return; }
                }

                ResetBall(scoringPlayer == 1 ? 2 : 1);
                return;
            }

            if (_room.Mode == GameMode.Standard && _room.Game.TimeRemaining <= 0)
            {
                var score1 = _room.Game.Score.GetValueOrDefault(1);
                var score2 = _room.Game.Score.GetValueOrDefault(2);
                if (score1 > score2) await EndMatch(GetPlayerNickname(1));
                else if (score2 > score1) await EndMatch(GetPlayerNickname(2));
                else await EndMatch(null);
            }
        }

        private void ActivatePowerUp(PowerUp powerUp, int activatorPlayerNum)
        {
            var ball = _room.Game.Ball;
            var activatorPaddle = _room.Game.Paddles.GetValueOrDefault(activatorPlayerNum);
            var opponentPlayerNum = activatorPlayerNum == 1 ? 2 : 1;
            var opponentPaddle = _room.Game.Paddles.GetValueOrDefault(opponentPlayerNum);
            switch (powerUp.Type)
            {
                case PowerUpType.WidePaddle:
                    if (activatorPaddle != null) { activatorPaddle.Width = GameConstants.PADDLE_WIDE_WIDTH; activatorPaddle.WidePaddleEndTime = DateTime.UtcNow.AddSeconds(8); }
                    break;
                case PowerUpType.FastBall:
                    ball.IsFast = true; ball.FastBallEndTime = DateTime.UtcNow.AddSeconds(6);
                    break;
                case PowerUpType.InvisibleBall:
                    ball.IsInvisible = true; ball.InvisibleEndTime = DateTime.UtcNow.AddSeconds(3);
                    break;
                case PowerUpType.ShrinkOpponentPaddle:
                    if (opponentPaddle != null) { opponentPaddle.Width = GameConstants.PADDLE_SHRUNK_WIDTH; opponentPaddle.ShrunkPaddleEndTime = DateTime.UtcNow.AddSeconds(8); }
                    break;
                case PowerUpType.TeleportBall:
                    var random = new Random();
                    ball.X = random.Next(50, GameConstants.CANVAS_WIDTH - 50);
                    ball.Y = random.Next(100, GameConstants.CANVAS_HEIGHT - 100);
                    double directionY = (opponentPlayerNum == 1) ? 1 : -1;
                    ball.Dx = (new Random().NextDouble() > 0.5 ? 1 : -1) * GameConstants.BALL_NORMAL_SPEED_X;
                    ball.Dy = directionY * GameConstants.BALL_NORMAL_SPEED_Y;
                    break;
            }
        }

        private void SpawnPowerUp()
        {
            var random = new Random();
            _room.Game.ActivePowerUps.Add(new PowerUp
            {
                X = random.Next(100, GameConstants.CANVAS_WIDTH - 100),
                Y = random.Next(150, GameConstants.CANVAS_HEIGHT - 150),
                Type = (PowerUpType)random.Next(0, Enum.GetNames(typeof(PowerUpType)).Length)
            });
        }

        private void ResetBall(int servingPlayer)
        {
            _rallyCount = 0;
            _room.Game.ActivePowerUps.Clear();
            var ball = _room.Game.Ball;
            _room.Game.Countdown = 3;
            _lastCountdownTick = DateTime.MinValue;
            ball.X = GameConstants.CANVAS_WIDTH / 2;
            ball.Y = GameConstants.CANVAS_HEIGHT / 2;
            ResetBallVelocity(servingPlayer, true);
        }

        private void ResetBallVelocity(int servingPlayer, bool isFirstServeOfPoint)
        {
            if (isFirstServeOfPoint)
            {
                _currentBallSpeed = _baseBallSpeed * GameConstants.BALL_STARTING_SPEED_MULTIPLIER;
            }
            double directionX = (new Random().NextDouble() > 0.5 ? 1 : -1);
            double directionY = servingPlayer == 1 ? 1 : -1;
            double baseMagnitude = Math.Sqrt(Math.Pow(GameConstants.BALL_NORMAL_SPEED_X, 2) + Math.Pow(GameConstants.BALL_NORMAL_SPEED_Y, 2));
            double normalizedBaseDx = (directionX * GameConstants.BALL_NORMAL_SPEED_X) / baseMagnitude;
            double normalizedBaseDy = (directionY * GameConstants.BALL_NORMAL_SPEED_Y) / baseMagnitude;
            _nextBallDx = normalizedBaseDx * _currentBallSpeed;
            _nextBallDy = normalizedBaseDy * _currentBallSpeed;
            _room.Game.Ball.Dx = 0;
            _room.Game.Ball.Dy = 0;
            foreach (var state in _aiStates.Values)
            {
                state.TargetX = GameConstants.CANVAS_WIDTH / 2;
            }
        }

        private async Task EndMatch(string? winnerNickname, double survivalScore = 0)
        {
            Stop();
            _room.State = RoomState.PostGame;

            var tasks = new List<Task>();
            foreach (var player in _room.Players.Values.Where(p => !p.IsAI))
            {
                var message = "";
                var result = "draw";

                if (_room.Mode == GameMode.SurvivalMarathon)
                {
                    message = $"Você sobreviveu por {TimeSpan.FromSeconds(survivalScore):m\\:ss}!";
                    result = "survival_end";

                    if (GameHub._userConnectionMap.TryGetValue(player.ConnectionId, out var userId))
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                            var account = await dbContext.Accounts.FindAsync(userId);
                            if (account != null && survivalScore > account.SurvivalHighScore)
                            {
                                account.SurvivalHighScore = survivalScore;
                                await dbContext.SaveChangesAsync();
                            }
                        }
                    }
                }
                else
                {
                    message = winnerNickname == null ? "A partida terminou em empate!" : $"O jogador {winnerNickname} venceu!";
                    result = (winnerNickname == null) ? "draw" : (player.Nickname == winnerNickname ? "victory" : "defeat");
                }

                tasks.Add(_hubContext.Clients.Client(player.ConnectionId).SendAsync("MatchEnded", new { Result = result, Message = message, Score = survivalScore }));
            }

            foreach (var p in _room.Players.Values) { p.HasReturnedToLobby = false; }
            await Task.WhenAll(tasks);
        }

        private string GetPlayerNickname(int playerNumber)
        {
            return _room.Players.Values.FirstOrDefault(p => p.Role == (PlayerRole)playerNumber)?.Nickname ?? $"Jogador {playerNumber}";
        }
    }

    public class GameHub : Hub
    {
        private static readonly ConcurrentDictionary<string, Room> _rooms = new();
        private static readonly ConcurrentDictionary<string, string> _playerRoomMap = new();
        private static readonly ConcurrentDictionary<string, GameLoopManager> _gameLoops = new();
        internal static readonly ConcurrentDictionary<string, string> _userConnectionMap = new();
        private readonly IHubContext<GameHub> _hubContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly GameDbContext _dbContext;
        private static readonly Dictionary<int, string> _aiDifficultyNames = new() { { 1, "Fácil" }, { 2, "Normal" }, { 3, "Difícil" } };

        public GameHub(IHubContext<GameHub> hubContext, GameDbContext dbContext, IServiceProvider serviceProvider)
        {
            _hubContext = hubContext;
            _dbContext = dbContext;
            _serviceProvider = serviceProvider;
        }

        public async Task<object> Register(string username, string password)
        {
            try
            {
                if (await _dbContext.Accounts.AnyAsync(a => a.Username == username))
                {
                    return new { success = false, message = "Este nome de usuário já existe." };
                }
                var salt = RandomNumberGenerator.GetBytes(16);
                var hashedPassword = HashPassword(password, salt);
                var newAccount = new UserAccount
                {
                    Username = username,
                    HashedPassword = Convert.ToBase64String(hashedPassword),
                    Salt = salt
                };
                _dbContext.Accounts.Add(newAccount);
                await _dbContext.SaveChangesAsync();
                _userConnectionMap[Context.ConnectionId] = newAccount.Id;
                return new { success = true, message = "Conta criada com sucesso!", user = new { id = newAccount.Id, username = newAccount.Username, title = newAccount.Title } };
            }
            catch (Exception ex)
            {
                return new { success = false, message = $"Erro interno do servidor: {ex.Message}" };
            }
        }

        public async Task<object> Login(string username, string password)
        {
            try
            {
                var account = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Username == username);
                if (account == null)
                {
                    return new { success = false, message = "Usuário ou senha inválidos." };
                }
                var hashedPassword = HashPassword(password, account.Salt);
                if (Convert.ToBase64String(hashedPassword) != account.HashedPassword)
                {
                    return new { success = false, message = "Usuário ou senha inválidos." };
                }
                _userConnectionMap[Context.ConnectionId] = account.Id;
                return new { success = true, message = "Login bem-sucedido!", user = new { id = account.Id, username = account.Username, title = account.Title } };
            }
            catch (Exception ex)
            {
                return new { success = false, message = $"Erro interno do servidor: {ex.Message}" };
            }
        }

        private byte[] HashPassword(string password, byte[] salt)
        {
            return new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256).GetBytes(20);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _userConnectionMap.TryRemove(Context.ConnectionId, out _);
            await LeaveRoom();
            await base.OnDisconnectedAsync(exception);
        }

        public async Task<object?> GetMyProfileData()
        {
            if (_userConnectionMap.TryGetValue(Context.ConnectionId, out var userId))
            {
                var account = await _dbContext.Accounts.FindAsync(userId);
                if (account != null)
                {
                    var registrationDateBrasilia = TimeZoneInfo.ConvertTimeFromUtc(account.RegistrationDate, TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"));
                    return new
                    {
                        username = account.Username,
                        registrationDate = registrationDateBrasilia.ToString("dd/MM/yyyy 'às' HH:mm"),
                        title = account.Title,
                        survivalHighScore = account.SurvivalHighScore
                    };
                }
            }
            return null;
        }

        public void UpdateInput(InputData input)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _gameLoops.TryGetValue(roomId, out var loop))
            {
                loop.UpdatePlayerInput(Context.ConnectionId, input);
            }
        }

        public async Task TogglePause(bool isPaused)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room))
            {
                if (!room.IsPublic && room.State == RoomState.InGame)
                {
                    room.Game.IsPaused = isPaused;
                    if (!isPaused) room.Game.Countdown = 3;
                    await Clients.Caller.SendAsync("GameStateUpdate", room.Game);
                }
            }
        }

        public async Task ReturnToLobby()
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room))
            {
                if (room.Players.TryGetValue(Context.ConnectionId, out var player))
                {
                    player.HasReturnedToLobby = true;
                }

                if (!room.IsPublic)
                {
                    if (_gameLoops.TryRemove(roomId, out var loop)) { loop.Stop(); }
                    _rooms.TryRemove(roomId, out _);
                    return;
                }

                var allPlayersReady = room.Players.Values.Where(p => !p.IsAI).All(p => p.HasReturnedToLobby);
                if (allPlayersReady)
                {
                    room.State = RoomState.Lobby;

                    var aiPlayersToRemove = room.Players.Values.Where(p => p.IsAI).ToList();
                    foreach (var ai in aiPlayersToRemove)
                    {
                        room.Players.TryRemove(ai.ConnectionId, out _);
                    }

                    foreach (var p in room.Players.Values) { p.Role = PlayerRole.Unassigned; }
                    if (_gameLoops.TryRemove(roomId, out var oldLoop)) { oldLoop.Stop(); }
                }
                await Clients.Group(roomId).SendAsync("UpdateRoom", room);
            }
        }

        public async Task StartSinglePlayerMatch(string nickname, int aiDifficulty, int timeLimitMinutes, int scoreLimit)
        {
            await LeaveRoom();
            var room = new Room { Name = $"Partida Clássica", IsPublic = false, HostConnectionId = Context.ConnectionId, AIDifficulty = aiDifficulty, TimeLimit = timeLimitMinutes * 60, ScoreLimit = scoreLimit, Mode = GameMode.Standard };
            var humanPlayer = new Player { ConnectionId = Context.ConnectionId, Nickname = nickname, Role = PlayerRole.Player1 };

            var aiNickname = _aiDifficultyNames.TryGetValue(aiDifficulty, out var diffName) ? $"I.A - {diffName}" : "I.A";
            var aiPlayer = new Player { ConnectionId = $"AI_{Guid.NewGuid()}", Nickname = aiNickname, Role = PlayerRole.Player2, IsAI = true, AIDifficulty = aiDifficulty };

            room.Players[humanPlayer.ConnectionId] = humanPlayer;
            room.Players[aiPlayer.ConnectionId] = aiPlayer;
            _rooms[room.Id] = room;
            _playerRoomMap[Context.ConnectionId] = room.Id;
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Id);
            room.State = RoomState.InGame;
            room.Game = new GameState();
            room.Game.Paddles[1] = new Paddle { X = 260, Y = 430, Width = GameConstants.PADDLE_NORMAL_WIDTH, Height = GameConstants.PADDLE_HEIGHT };
            room.Game.Paddles[2] = new Paddle { X = 260, Y = 10, Width = GameConstants.PADDLE_NORMAL_WIDTH, Height = GameConstants.PADDLE_HEIGHT };
            room.Game.Ball = new Ball { X = 300, Y = 225, Radius = GameConstants.BALL_RADIUS, Dx = 0, Dy = 0 };
            room.Game.Score[1] = 0; room.Game.Score[2] = 0;
            var gameLoop = new GameLoopManager(room, _hubContext, _serviceProvider);
            _gameLoops[room.Id] = gameLoop;
            gameLoop.StartMatchInitialization();
            gameLoop.Start();
            await Clients.Caller.SendAsync("MatchStarted", room);
        }

        public async Task StartSurvivalMatch(string nickname, int aiDifficulty)
        {
            await LeaveRoom();
            var room = new Room
            {
                Name = "Maratona de Sobrevivência",
                IsPublic = false,
                HostConnectionId = Context.ConnectionId,
                AIDifficulty = aiDifficulty,
                Mode = GameMode.SurvivalMarathon
            };
            var humanPlayer = new Player { ConnectionId = Context.ConnectionId, Nickname = nickname, Role = PlayerRole.Player1 };
            var aiNickname = _aiDifficultyNames.TryGetValue(aiDifficulty, out var diffName) ? $"I.A - {diffName}" : "I.A";
            var aiPlayer = new Player { ConnectionId = $"AI_{Guid.NewGuid()}", Nickname = aiNickname, Role = PlayerRole.Player2, IsAI = true, AIDifficulty = aiDifficulty };

            room.Players[humanPlayer.ConnectionId] = humanPlayer;
            room.Players[aiPlayer.ConnectionId] = aiPlayer;
            _rooms[room.Id] = room;
            _playerRoomMap[Context.ConnectionId] = room.Id;
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Id);
            room.State = RoomState.InGame;
            room.Game = new GameState { PlayerLives = 3 };
            room.Game.Paddles[1] = new Paddle { X = 260, Y = 430, Width = GameConstants.PADDLE_NORMAL_WIDTH, Height = GameConstants.PADDLE_HEIGHT };
            room.Game.Paddles[2] = new Paddle { X = 260, Y = 10, Width = GameConstants.PADDLE_NORMAL_WIDTH, Height = GameConstants.PADDLE_HEIGHT };
            room.Game.Ball = new Ball { X = 300, Y = 225, Radius = GameConstants.BALL_RADIUS, Dx = 0, Dy = 0 };

            var gameLoop = new GameLoopManager(room, _hubContext, _serviceProvider);
            _gameLoops[room.Id] = gameLoop;
            gameLoop.StartMatchInitialization();
            gameLoop.Start();
            await Clients.Caller.SendAsync("MatchStarted", room);
        }

        public async Task CreateRoom(string roomName, string nickname, string? password)
        {
            await LeaveRoom();
            var room = new Room { Name = roomName, IsPublic = string.IsNullOrEmpty(password), Password = password, HostConnectionId = Context.ConnectionId };
            _rooms[room.Id] = room;
            await JoinRoom(room.Id, nickname, password);
        }

        public async Task JoinRoom(string roomId, string nickname, string? password)
        {
            await LeaveRoom();
            if (!_rooms.TryGetValue(roomId, out var room) || (room.Players.Count >= 4 && !room.Players.ContainsKey(Context.ConnectionId))) { await Clients.Caller.SendAsync("Error", "Sala não encontrada ou está cheia."); return; }
            if (!room.IsPublic && room.Password != password) { await Clients.Caller.SendAsync("Error", "Senha incorreta."); return; }
            var player = new Player { ConnectionId = Context.ConnectionId, Nickname = nickname };
            room.Players[Context.ConnectionId] = player;
            _playerRoomMap[Context.ConnectionId] = roomId;
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            await Clients.Caller.SendAsync("JoinedRoom", room);
            await Clients.Group(roomId).SendAsync("UpdateRoom", room);
            await NotifyRoomListChanged();
        }

        public async Task LeaveRoom()
        {
            if (_playerRoomMap.TryRemove(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room))
            {
                await RemovePlayerFromRoom(Context.ConnectionId, roomId, room);
            }
        }

        public async Task GetRoomList() => await NotifyRoomListChangedForCaller();

        public async Task ChooseRole(PlayerRole role)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room) && room.Players.TryGetValue(Context.ConnectionId, out var player))
            {
                if (role != PlayerRole.Spectator && room.Players.Values.Any(p => p.Role == role))
                {
                    await Clients.Caller.SendAsync("Error", "Este papel já está ocupado.");
                    return;
                }
                player.Role = role;
                await Clients.Group(roomId).SendAsync("UpdateRoom", room);
            }
        }

        public async Task LeaveRole()
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room) && room.Players.TryGetValue(Context.ConnectionId, out var player))
            {
                player.Role = PlayerRole.Unassigned;
                await Clients.Group(roomId).SendAsync("UpdateRoom", room);
            }
        }

        public async Task RemoveFromRole(string targetConnectionId)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) &&
                _rooms.TryGetValue(roomId, out var room) &&
                Context.ConnectionId == room.HostConnectionId &&
                room.Players.TryGetValue(targetConnectionId, out var targetPlayer) &&
                !targetPlayer.IsAI)
            {
                targetPlayer.Role = PlayerRole.Unassigned;
                await Clients.Group(roomId).SendAsync("UpdateRoom", room);
            }
        }

        public async Task RemoveAIPlayer(string targetConnectionId)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) &&
                _rooms.TryGetValue(roomId, out var room) &&
                Context.ConnectionId == room.HostConnectionId &&
                room.Players.TryGetValue(targetConnectionId, out var targetPlayer) &&
                targetPlayer.IsAI)
            {
                await RemovePlayerFromRoom(targetConnectionId, roomId, room);
            }
        }

        public async Task StartMatch()
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room) && Context.ConnectionId == room.HostConnectionId)
            {
                if (room.State != RoomState.Lobby) return;
                if (room.Players.Values.Any(p => !p.IsAI && !p.HasReturnedToLobby)) { await Clients.Caller.SendAsync("Error", "Aguarde todos os jogadores retornarem ao lobby."); return; }
                if (!room.Players.Values.Any(p => p.Role == PlayerRole.Player1) || !room.Players.Values.Any(p => p.Role == PlayerRole.Player2)) { await Clients.Caller.SendAsync("Error", "É preciso ter um Jogador 1 e um Jogador 2 para iniciar."); return; }
                foreach (var p in room.Players.Values) { p.HasReturnedToLobby = false; }
                room.State = RoomState.InGame;
                room.Game = new GameState();
                room.Game.Paddles[1] = new Paddle { X = 260, Y = 430, Width = GameConstants.PADDLE_NORMAL_WIDTH, Height = GameConstants.PADDLE_HEIGHT };
                room.Game.Paddles[2] = new Paddle { X = 260, Y = 10, Width = GameConstants.PADDLE_NORMAL_WIDTH, Height = GameConstants.PADDLE_HEIGHT };
                room.Game.Ball = new Ball { X = 300, Y = 225, Radius = GameConstants.BALL_RADIUS, Dx = 0, Dy = 0 };
                room.Game.Score[1] = 0; room.Game.Score[2] = 0;
                if (_gameLoops.TryRemove(roomId, out var oldLoop)) { oldLoop.Stop(); }
                var gameLoop = new GameLoopManager(room, _hubContext, _serviceProvider);
                _gameLoops[room.Id] = gameLoop;
                gameLoop.StartMatchInitialization();
                gameLoop.Start();
                await Clients.Group(roomId).SendAsync("MatchStarted", room);
                await NotifyRoomListChanged();
            }
        }

        public async Task UpdateRoomSettings(int scoreLimit, int timeLimit)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room) && Context.ConnectionId == room.HostConnectionId)
            {
                room.ScoreLimit = scoreLimit;
                room.TimeLimit = timeLimit * 60;
                await Clients.Group(roomId).SendAsync("UpdateRoom", room);
            }
        }

        public async Task KickPlayer(string targetConnectionId)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room) && Context.ConnectionId == room.HostConnectionId && room.Players.ContainsKey(targetConnectionId))
            {
                await Clients.Client(targetConnectionId).SendAsync("Kicked", "Você foi removido da sala pelo host.");
                await RemovePlayerFromRoom(targetConnectionId, roomId, room);
            }
        }

        public async Task AddAI(PlayerRole role, int difficulty)
        {
            if (_playerRoomMap.TryGetValue(Context.ConnectionId, out var roomId) && _rooms.TryGetValue(roomId, out var room) && Context.ConnectionId == room.HostConnectionId)
            {
                if (role == PlayerRole.Player1 || role == PlayerRole.Player2)
                {
                    if (room.Players.Values.Any(p => p.Role == role)) { await Clients.Caller.SendAsync("Error", "Este slot de jogador já está ocupado."); return; }

                    var aiNickname = _aiDifficultyNames.TryGetValue(difficulty, out var diffName) ? $"I.A - {diffName}" : "I.A";
                    var aiPlayer = new Player { ConnectionId = $"AI_{role}_{Guid.NewGuid()}", Nickname = aiNickname, Role = role, IsAI = true, AIDifficulty = difficulty };

                    room.Players[aiPlayer.ConnectionId] = aiPlayer;
                    room.AIDifficulty = difficulty;
                    await Clients.Group(roomId).SendAsync("UpdateRoom", room);
                }
            }
        }

        private async Task RemovePlayerFromRoom(string connectionId, string roomId, Room room)
        {
            if (room.Players.TryRemove(connectionId, out var player))
            {
                if (!player.IsAI) await Groups.RemoveFromGroupAsync(connectionId, roomId);
                if (room.Players.Values.Count(p => !p.IsAI) == 0 && room.IsPublic)
                {
                    if (_gameLoops.TryRemove(roomId, out var loop)) { loop.Stop(); }
                    _rooms.TryRemove(roomId, out _);
                }
                else if (connectionId == room.HostConnectionId)
                {
                    room.HostConnectionId = room.Players.Values.Where(p => !p.IsAI).OrderBy(p => p.JoinedAt).FirstOrDefault()?.ConnectionId ?? string.Empty;
                }
                await Clients.Group(roomId).SendAsync("UpdateRoom", room);
                await NotifyRoomListChanged();
            }
        }

        private async Task NotifyRoomListChanged()
        {
            var roomList = _rooms.Values.Where(r => r.State == RoomState.Lobby).Select(r => new { r.Id, r.Name, r.IsPublic, PlayersCount = r.Players.Values.Count });
            await Clients.All.SendAsync("ReceiveRoomList", roomList);
        }

        private async Task NotifyRoomListChangedForCaller()
        {
            var roomList = _rooms.Values.Where(r => r.State == RoomState.Lobby).Select(r => new { r.Id, r.Name, r.IsPublic, PlayersCount = r.Players.Values.Count });
            await Clients.Caller.SendAsync("ReceiveRoomList", roomList);
        }
    }
}