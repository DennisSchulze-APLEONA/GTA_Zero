using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GTA_Zero
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer gameTimer = new DispatcherTimer();
        private readonly HashSet<Key> pressedKeys = new HashSet<Key>();
        private readonly Random rng = new Random();
        private Point mousePos;

        // Game Stats
        private int score = 0;
        private int wantedLevel = 0; // 0 to 5
        private int wantedDecayTimer = 0;
        private bool isGameOver = false;

        // Player State
        private Ellipse playerShape;
        private Point playerPos;
        private readonly double playerSpeed = 4.5;
        private Car currentCar = null;

        // Entity Collections
        private readonly List<Rectangle> walls = new List<Rectangle>();
        private readonly List<Car> cars = new List<Car>();
        private readonly List<Npc> npcs = new List<Npc>();
        private readonly List<Bullet> bullets = new List<Bullet>();
        private readonly List<Explosion> explosions = new List<Explosion>();

        private class Car
        {
            public Rectangle Shape;
            public RotateTransform Rotate = new RotateTransform();
            public Point Pos;
            public double Angle;
            public double Speed;
            public double Health = 100;
            public bool IsPolice;
            public Brush BaseColor;
        }

        private class Npc
        {
            public Ellipse Shape;
            public Point Pos;
            public Vector Direction;
            public int MoveTimer;
        }

        private class Bullet
        {
            public Ellipse Shape;
            public Point Pos;
            public Vector Velocity;
            public int Life = 65;
        }

        private class Explosion
        {
            public Ellipse Shape;
            public Point Pos;
            public double Radius = 10;
            public int Life = 22;
        }

        public MainWindow()
        {
            InitializeComponent();
            StartNewGame();

            gameTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            gameTimer.Tick += GameLoop;
            gameTimer.Start();
        }

        private void StartNewGame()
        {
            // Clear existing dynamic objects
            WorldCanvas.Children.Clear();
            walls.Clear();
            cars.Clear();
            npcs.Clear();
            bullets.Clear();
            explosions.Clear();

            score = 0;
            wantedLevel = 0;
            isGameOver = false;
            currentCar = null;
            GameOverBanner.Visibility = Visibility.Collapsed;

            // Rebuild Streets
            AddStreet(0, 250, 2000, 140);
            AddStreet(0, 950, 2000, 140);
            AddStreet(0, 1700, 2000, 140);
            AddStreet(380, 0, 140, 2000);
            AddStreet(1180, 0, 140, 2000);

            // Rebuild Buildings
            AddWall(60, 50, 260, 150);
            AddWall(580, 50, 540, 150);
            AddWall(1380, 50, 540, 150);

            AddWall(60, 440, 260, 450);
            AddWall(580, 440, 540, 450);
            AddWall(1380, 440, 540, 450);

            AddWall(60, 1140, 260, 500);
            AddWall(580, 1140, 540, 500);
            AddWall(1380, 1140, 540, 500);

            // Spawn Stealable Civilian Cars
            Brush[] carColors = { Brushes.Crimson, Brushes.Orange, Brushes.Gold, Brushes.MediumPurple, Brushes.Cyan, Brushes.WhiteSmoke };
            Point[] carSpawns =
            {
                new Point(520, 320), new Point(850, 300), new Point(1450, 340),
                new Point(450, 700), new Point(1250, 800), new Point(700, 1020),
                new Point(1500, 1020), new Point(450, 1400), new Point(900, 1760)
            };

            for (int i = 0; i < carSpawns.Length; i++)
            {
                SpawnCar(carSpawns[i], carColors[i % carColors.Length], isPolice: false);
            }

            // Spawn Pedestrians
            for (int i = 0; i < 18; i++)
            {
                SpawnNpc();
            }

            // Spawn Player
            playerPos = new Point(450, 320);
            playerShape = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = Brushes.DodgerBlue,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            WorldCanvas.Children.Add(playerShape);
        }

        private void AddStreet(double x, double y, double w, double h)
        {
            var street = new Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromRgb(68, 68, 68)) };
            Canvas.SetLeft(street, x);
            Canvas.SetTop(street, y);
            WorldCanvas.Children.Add(street);
        }

        private void AddWall(double x, double y, double w, double h)
        {
            var wall = new Rectangle
            {
                Width = w,
                Height = h,
                Fill = Brushes.DimGray,
                Stroke = Brushes.Black,
                StrokeThickness = 3
            };
            Canvas.SetLeft(wall, x);
            Canvas.SetTop(wall, y);
            WorldCanvas.Children.Add(wall);
            walls.Add(wall);
        }

        private Car SpawnCar(Point pos, Brush color, bool isPolice)
        {
            var car = new Car
            {
                Pos = pos,
                Angle = rng.Next(0, 4) * 90,
                BaseColor = color,
                IsPolice = isPolice,
                Shape = new Rectangle
                {
                    Width = 46,
                    Height = 24,
                    Fill = color,
                    Stroke = isPolice ? Brushes.DeepSkyBlue : Brushes.Black,
                    StrokeThickness = isPolice ? 3 : 2,
                    RadiusX = 4,
                    RadiusY = 4,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                }
            };
            car.Shape.RenderTransform = car.Rotate;
            WorldCanvas.Children.Add(car.Shape);
            cars.Add(car);
            return car;
        }

        private void SpawnNpc()
        {
            Point spawn;
            do
            {
                spawn = new Point(rng.Next(100, 1900), rng.Next(100, 1900));
            } while (HitsWall(new Rect(spawn.X - 10, spawn.Y - 10, 20, 20)));

            var shape = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.LimeGreen,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            WorldCanvas.Children.Add(shape);

            npcs.Add(new Npc
            {
                Shape = shape,
                Pos = spawn,
                Direction = new Vector(1, 0),
                MoveTimer = 0
            });
        }

        private void GameLoop(object sender, EventArgs e)
        {
            if (isGameOver) return;

            if (currentCar != null)
                UpdatePlayerDriving();
            else
                UpdatePlayerWalking();

            UpdateAllCars();
            UpdatePoliceSpawning();
            UpdateNpcs();
            UpdateBullets();
            UpdateExplosions();
            RenderEntities();
            UpdateCamera();
            UpdateHud();
        }

        private void UpdatePlayerWalking()
        {
            Vector move = new Vector(0, 0);
            if (pressedKeys.Contains(Key.W)) move.Y -= 1;
            if (pressedKeys.Contains(Key.S)) move.Y += 1;
            if (pressedKeys.Contains(Key.A)) move.X -= 1;
            if (pressedKeys.Contains(Key.D)) move.X += 1;

            if (move.Length > 0)
            {
                move.Normalize();
                Point nextPos = playerPos + (move * playerSpeed);
                Rect playerBox = new Rect(nextPos.X - 9, nextPos.Y - 9, 18, 18);

                if (!HitsWall(playerBox) && IsInsideWorld(nextPos))
                    playerPos = nextPos;
            }
        }

        private void UpdatePlayerDriving()
        {
            if (pressedKeys.Contains(Key.W)) currentCar.Speed += 0.28;
            if (pressedKeys.Contains(Key.S)) currentCar.Speed -= 0.28;

            currentCar.Speed = Math.Clamp(currentCar.Speed, -4.5, 11.0);

            if (Math.Abs(currentCar.Speed) > 0.3)
            {
                double turnDir = currentCar.Speed >= 0 ? 1 : -1;
                if (pressedKeys.Contains(Key.A)) currentCar.Angle -= 3.8 * turnDir;
                if (pressedKeys.Contains(Key.D)) currentCar.Angle += 3.8 * turnDir;
            }

            playerPos = currentCar.Pos;
        }

        private void UpdateAllCars()
        {
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                var car = cars[i];

                // Police AI Chase Behavior
                if (car.IsPolice && car != currentCar)
                {
                    if (wantedLevel > 0)
                    {
                        Vector toPlayer = playerPos - car.Pos;
                        double targetAngle = Math.Atan2(toPlayer.Y, toPlayer.X) * 180.0 / Math.PI;
                        double angleDiff = NormalizeAngle(targetAngle - car.Angle);

                        car.Angle += Math.Clamp(angleDiff, -4.0, 4.0);
                        car.Speed = Math.Min(car.Speed + 0.25, 8.5 + wantedLevel * 0.4);

                        // Police flashers effect
                        car.Shape.Stroke = (DateTime.Now.Millisecond / 150) % 2 == 0 ? Brushes.Red : Brushes.DeepSkyBlue;
                    }
                    else
                    {
                        car.Speed *= 0.94;
                    }
                }
                else if (car != currentCar)
                {
                    // Parked civilian car friction
                    car.Speed *= 0.94;
                }
                else
                {
                    // Player car natural friction
                    car.Speed *= 0.96;
                }
                // Move car
                double rad = car.Angle * Math.PI / 180.0;
                Point nextPos = new Point(
                car.Pos.X + Math.Cos(rad) * car.Speed,
                car.Pos.Y + Math.Sin(rad) * car.Speed
                );
                Rect carBox = new Rect(nextPos.X - 16, nextPos.Y - 16, 32, 32);
                // Wall Crash
                if (HitsWall(carBox) || !IsInsideWorld(nextPos))
                {
                    double impact = Math.Abs(car.Speed);
                    if (impact > 2.5)
                    {
                        DamageCar(car, impact * 2.5);
                        if (car == currentCar) AddScore((int)(impact * 5));
                    }
                    car.Speed = -car.Speed * 0.5;
                }
                else
                {
                    car.Pos = nextPos;
                }
                // Car-to-Car Crashes
                for (int j = i - 1; j >= 0; j--)
                {
                    var other = cars[j];
                    Vector diff = car.Pos - other.Pos;
                    if (diff.Length < 34)
                    {
                        double relSpeed = Math.Abs(car.Speed) + Math.Abs(other.Speed);
                        if (relSpeed > 2.0)
                        {
                            DamageCar(car, relSpeed * 3.0);
                            DamageCar(other, relSpeed * 3.0);
                            if (car == currentCar || other == currentCar)
                            {
                                AddScore((int)(relSpeed * 15));
                                IncreaseWanted(1);
                            }
                        }
                        // Elastic bump
                        if (diff.Length > 0) diff.Normalize();
                        car.Pos += diff * 4;
                        other.Pos -= diff * 4;
                        car.Speed *= -0.4;
                        other.Speed *= -0.4;
                    }
                }
                // Check if Police Car rams Player on foot
                if (car.IsPolice && currentCar == null && Math.Abs(car.Speed) > 3.0 && (car.Pos - playerPos).Length < 26)
                {
                    TriggerGameOver();
                }
                // Explode if health reaches 0
                if (car.Health <= 0)
                {
                    ExplodeCar(car);
                    cars.RemoveAt(i);
                }
            }
        }
        private void DamageCar(Car car, double amount)
        {
            car.Health -= amount;
            if (car.Health < 40)
                car.Shape.Fill = Brushes.DarkSlateGray; // Smoke/charred look
        }
        private void ExplodeCar(Car car)
        {
            WorldCanvas.Children.Remove(car.Shape);
            SpawnExplosion(car.Pos);
            AddScore(car.IsPolice ? 500 : 250);
            IncreaseWanted(1);
            // If player was inside when it blew up -> Wasted!
            if (car == currentCar)
            {
                currentCar = null;
                TriggerGameOver();
            }
            else if ((car.Pos - playerPos).Length < 90)
            {
                TriggerGameOver();
            }
            // Respawn a replacement car far away so the map never runs empty
            Point respawn = new Point(rng.Next(200, 1800), 320);
            SpawnCar(respawn, car.IsPolice ? Brushes.Black : car.BaseColor, car.IsPolice);
        }
        private void UpdatePoliceSpawning()
        {
            if (wantedLevel > 0)
            {
                wantedDecayTimer++;
                if (wantedDecayTimer > 600) // ~10 seconds without crime lowers wanted level
                {
                    wantedLevel--;
                    wantedDecayTimer = 0;
                }
                int activeCops = 0;
                foreach (var c in cars)
                    if (c.IsPolice) activeCops++;
                // Keep active cop cars equal to wantedLevel
                if (activeCops < wantedLevel)
                {
                    double angle = rng.NextDouble() * Math.PI * 2;
                    Point spawn = new Point(
                    Math.Clamp(playerPos.X + Math.Cos(angle) * 650, 80, 1920),
                    Math.Clamp(playerPos.Y + Math.Sin(angle) * 650, 80, 1920)
                    );
                    if (!HitsWall(new Rect(spawn.X - 20, spawn.Y - 20, 40, 40)))
                    {
                        SpawnCar(spawn, Brushes.Black, isPolice: true);
                    }
                }
            }
        }
        private void UpdateNpcs()
        {
            for (int i = npcs.Count - 1; i >= 0; i--)
            {
                var npc = npcs[i];
                npc.MoveTimer--;
                if (npc.MoveTimer <= 0)
                {
                    double angle = rng.NextDouble() * Math.PI * 2;
                    npc.Direction = new Vector(Math.Cos(angle), Math.Sin(angle));
                    npc.MoveTimer = rng.Next(40, 120);
                }
                Point nextPos = npc.Pos + npc.Direction * 1.5;
                Rect npcBox = new Rect(nextPos.X - 8, nextPos.Y - 8, 16, 16);
                if (!HitsWall(npcBox) && IsInsideWorld(nextPos))
                    npc.Pos = nextPos;
                else
                    npc.MoveTimer = 0;
                // Run over by player's car
                if (currentCar != null && Math.Abs(currentCar.Speed) > 2.2 && (npc.Pos - currentCar.Pos).Length < 25)
                {
                    WorldCanvas.Children.Remove(npc.Shape);
                    npcs.RemoveAt(i);
                    AddScore(100);
                    IncreaseWanted(1);
                    SpawnNpc();
                }
            }
        }
        private void UpdateBullets()
        {
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                b.Pos += b.Velocity;
                b.Life--;
                Rect bulletBox = new Rect(b.Pos.X - 3, b.Pos.Y - 3, 6, 6);
                bool hitSomething = false;
                // Hit NPCs
                for (int j = npcs.Count - 1; j >= 0; j--)
                {
                    if ((npcs[j].Pos - b.Pos).Length < 13)
                    {
                        WorldCanvas.Children.Remove(npcs[j].Shape);
                        npcs.RemoveAt(j);
                        AddScore(100);
                        IncreaseWanted(1);
                        SpawnNpc();
                        hitSomething = true;
                        break;
                    }
                }
                // Hit Cars (ignore the car player is currently driving)
                if (!hitSomething)
                {
                    for (int j = cars.Count - 1; j >= 0; j--)
                    {
                        var car = cars[j];
                        if (car == currentCar) continue;
                        if ((car.Pos - b.Pos).Length < 24)
                        {
                            DamageCar(car, 20);
                            AddScore(25);
                            if (car.IsPolice) IncreaseWanted(1);
                            hitSomething = true;
                            break;
                        }
                    }
                }
                if (b.Life <= 0 || hitSomething || HitsWall(bulletBox))
                {
                    WorldCanvas.Children.Remove(b.Shape);
                    bullets.RemoveAt(i);
                    continue;
                }
                Canvas.SetLeft(b.Shape, b.Pos.X - 3);
                Canvas.SetTop(b.Shape, b.Pos.Y - 3);
            }
        }
        private void SpawnExplosion(Point pos)
        {
            var shape = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.OrangeRed,
                Stroke = Brushes.Yellow,
                StrokeThickness = 3,
                Opacity = 0.9
            };
            WorldCanvas.Children.Add(shape);
            explosions.Add(new Explosion { Shape = shape, Pos = pos });
        }
        private void UpdateExplosions()
        {
            for (int i = explosions.Count - 1; i >= 0; i--)
            {
                var ex = explosions[i];
                ex.Life--;
                ex.Radius += 4.5;
                ex.Shape.Width = ex.Radius * 2;
                ex.Shape.Height = ex.Radius * 2;
                ex.Shape.Opacity = ex.Life / 22.0;
                Canvas.SetLeft(ex.Shape, ex.Pos.X - ex.Radius);
                Canvas.SetTop(ex.Shape, ex.Pos.Y - ex.Radius);
                if (ex.Life <= 0)
                {
                    WorldCanvas.Children.Remove(ex.Shape);
                    explosions.RemoveAt(i);
                }
            }
        }
        private void AddScore(int points)
        {
            score += points;
        }
        private void IncreaseWanted(int amount)
        {
            wantedLevel = Math.Min(5, wantedLevel + amount);
            wantedDecayTimer = 0;
        }
        private void TriggerGameOver()
        {
            isGameOver = true;
            FinalScoreText.Text = $"Final Score: ${score:N0}";
            GameOverBanner.Visibility = Visibility.Visible;
        }
        private void RenderEntities()
        {
            foreach (var car in cars)
            {
                Canvas.SetLeft(car.Shape, car.Pos.X - car.Shape.Width / 2);
                Canvas.SetTop(car.Shape, car.Pos.Y - car.Shape.Height / 2);
                car.Rotate.Angle = car.Angle;
            }
            playerShape.Visibility = (currentCar != null || isGameOver) ? Visibility.Hidden : Visibility.Visible;
            Canvas.SetLeft(playerShape, playerPos.X - playerShape.Width / 2);
            Canvas.SetTop(playerShape, playerPos.Y - playerShape.Height / 2);
            foreach (var npc in npcs)
            {
                Canvas.SetLeft(npc.Shape, npc.Pos.X - 8);
                Canvas.SetTop(npc.Shape, npc.Pos.Y - 8);
            }
        }
        private void UpdateCamera()
        {
            double camX = (ViewportCanvas.ActualWidth / 2) - playerPos.X;
            double camY = (ViewportCanvas.ActualHeight / 2) - playerPos.Y;
            camX = Math.Clamp(camX, -(WorldCanvas.Width - ViewportCanvas.ActualWidth), 0);
            camY = Math.Clamp(camY, -(WorldCanvas.Height - ViewportCanvas.ActualHeight), 0);
            Canvas.SetLeft(WorldCanvas, camX);
            Canvas.SetTop(WorldCanvas, camY);
        }
        private void UpdateHud()
        {
            ScoreText.Text = $"SCORE: ${score:N0}";
            string stars = "";
            for (int i = 1; i <= 5; i++)
                stars += (i <= wantedLevel) ? "★ " : "☆ ";
            WantedText.Text = $"WANTED: {stars.Trim()}";
            WantedText.Foreground = wantedLevel > 0 ? Brushes.OrangeRed : Brushes.DeepSkyBlue;
            if (currentCar != null)
            {
                CarHpText.Text = $"VEHICLE HEALTH: {Math.Max(0, (int)currentCar.Health)}%";
                CarHpText.Foreground = currentCar.Health < 35 ? Brushes.Red : Brushes.LightGreen;
            }
            else
            {
                CarHpText.Text = "ON FOOT (Press F near a car to steal)";
                CarHpText.Foreground = Brushes.White;
            }
        }
        private bool HitsWall(Rect hitbox)
        {
            foreach (var wall in walls)
            {
                Rect wallBox = new Rect(Canvas.GetLeft(wall), Canvas.GetTop(wall), wall.Width, wall.Height);
                if (hitbox.IntersectsWith(wallBox))
                    return true;
            }
            return false;
        }
        private bool IsInsideWorld(Point p) => p.X > 20 && p.X < 1980 && p.Y > 20 && p.Y < 1980;
        private double NormalizeAngle(double angle)
        {
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            pressedKeys.Add(e.Key);
            if (e.Key == Key.R && isGameOver)
            {
                StartNewGame();
                return;
            }
            if (e.Key == Key.F && !isGameOver)
            {
                if (currentCar != null)
                {
                    // Exit Car
                    playerPos = new Point(currentCar.Pos.X, currentCar.Pos.Y - 28);
                    currentCar = null;
                }
                else
                {
                    // Find closest car within reach
                    Car closest = null;
                    double minDist = 52;
                    foreach (var car in cars)
                    {
                        double dist = (playerPos - car.Pos).Length;
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closest = car;
                        }
                    }
                    if (closest != null)
                    {
                        currentCar = closest;
                        AddScore(50); // Carjack bonus
                        if (closest.IsPolice) IncreaseWanted(2); // Stealing a cop car raises wanted level!
                    }
                }
            }
        }
        private void Window_KeyUp(object sender, KeyEventArgs e) => pressedKeys.Remove(e.Key);
        private void Window_MouseMove(object sender, MouseEventArgs e) => mousePos = e.GetPosition(WorldCanvas);
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isGameOver || e.LeftButton != MouseButtonState.Pressed) return;
            mousePos = e.GetPosition(WorldCanvas);
            Vector dir = mousePos - playerPos;
            if (dir.Length == 0) return;
            dir.Normalize();
            var shape = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Yellow };
            WorldCanvas.Children.Add(shape);
            bullets.Add(new Bullet
            {
                Shape = shape,
                Pos = playerPos,
                Velocity = dir * 16.0
            });
        }
    }
}