using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
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
        private bool isMouseDown = false;

        // Game Stats
        private int score = 0;
        private int wantedLevel = 0;
        private int wantedDecayTimer = 0;
        private bool isGameOver = false;

        // Player & Weapons
        private Ellipse playerShape;
        private Point playerPos;
        private readonly double playerSpeed = 4.5;
        private Car currentCar = null;

        private enum WeaponType { Pistol, Shotgun, Smg, RocketLauncher }
        private WeaponType currentWeapon = WeaponType.Pistol;
        private int weaponAmmo = -1; // -1 = infinite
        private int fireCooldown = 0;

        // Entity Lists
        private readonly List<Rectangle> walls = new List<Rectangle>();
        private readonly List<Car> cars = new List<Car>();
        private readonly List<Npc> npcs = new List<Npc>();
        private readonly List<Projectile> projectiles = new List<Projectile>();
        private readonly List<Explosion> explosions = new List<Explosion>();
        private readonly List<Pickup> pickups = new List<Pickup>();

        // Procedural Sound Players
        private SoundPlayer sfxShoot;
        private SoundPlayer sfxExplosion;
        private SoundPlayer sfxCrash;
        private SoundPlayer sfxPickup;
        private SoundPlayer sfxSiren;
        private int sirenTimer = 0;

        private class Car
        {
            public Rectangle Shape;
            public RotateTransform Rotate = new RotateTransform();
            public Point Pos;
            public double Angle;
            public double Speed;
            public double Health = 100;
            public bool IsPolice;
            public bool IsTraffic;
            public int TurnCooldown;
            public Brush BaseColor;
        }

        private class Npc
        {
            public Ellipse Shape;
            public Point Pos;
            public Vector Direction;
            public int MoveTimer;
        }

        private class Projectile
        {
            public Ellipse Shape;
            public Point Pos;
            public Vector Velocity;
            public int Life;
            public double Damage;
            public bool IsRocket;
        }

        private class Explosion
        {
            public Ellipse Shape;
            public Point Pos;
            public double Radius = 10;
            public int Life = 22;
        }

        private class Pickup
        {
            public Border Visual;
            public Point Pos;
            public WeaponType Weapon;
            public int Ammo;
        }

        public MainWindow()
        {
            InitializeComponent();
            InitProceduralSounds();
            StartNewGame();

            gameTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            gameTimer.Tick += GameLoop;
            gameTimer.Start();
        }

        // Generates retro WAV sound effects in memory—no external audio files needed!
        private void InitProceduralSounds()
        {
            sfxShoot = CreateSynthSound(durationSec: 0.08, startFreq: 650, endFreq: 140, noiseMix: 0.6, volume: 0.35);
            sfxExplosion = CreateSynthSound(durationSec: 0.45, startFreq: 160, endFreq: 35, noiseMix: 0.85, volume: 0.65);
            sfxCrash = CreateSynthSound(durationSec: 0.16, startFreq: 220, endFreq: 70, noiseMix: 0.75, volume: 0.40);
            sfxPickup = CreateSynthSound(durationSec: 0.15, startFreq: 440, endFreq: 880, noiseMix: 0.0, volume: 0.35);
            sfxSiren = CreateSynthSound(durationSec: 0.25, startFreq: 720, endFreq: 540, noiseMix: 0.0, volume: 0.20);
        }

        private SoundPlayer CreateSynthSound(double durationSec, double startFreq, double endFreq, double noiseMix, double volume)
        {
            int sampleRate = 22050;
            int samples = (int)(sampleRate * durationSec);
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Standard 44-byte PCM WAV header
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + samples * 2);
            writer.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(samples * 2);

            double phase = 0;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / samples;
                double envelope = 1.0 - t;
                double freq = startFreq + (endFreq - startFreq) * t;
                phase += 2.0 * Math.PI * freq / sampleRate;

                double tone = Math.Sin(phase);
                double noise = (rng.NextDouble() * 2.0) - 1.0;
                double sample = ((tone * (1.0 - noiseMix)) + (noise * noiseMix)) * envelope * volume;

                short pcm = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
                writer.Write(pcm);
            }

            ms.Position = 0;
            var player = new SoundPlayer(ms);
            player.Load();
            return player;
        }

        private void StartNewGame()
        {
            WorldCanvas.Children.Clear();
            walls.Clear();
            cars.Clear();
            npcs.Clear();
            projectiles.Clear();
            explosions.Clear();
            pickups.Clear();

            score = 0;
            wantedLevel = 0;
            isGameOver = false;
            currentCar = null;
            currentWeapon = WeaponType.Pistol;
            weaponAmmo = -1;
            GameOverBanner.Visibility = Visibility.Collapsed;

            // Streets (Grid of 3 horizontal + 2 vertical roads)
            AddStreet(0, 250, 2000, 140);
            AddStreet(0, 950, 2000, 140);
            AddStreet(0, 1700, 2000, 140);
            AddStreet(380, 0, 140, 2000);
            AddStreet(1180, 0, 140, 2000);

            // City Blocks
            AddWall(60, 50, 260, 150);
            AddWall(580, 50, 540, 150);
            AddWall(1380, 50, 540, 150);

            AddWall(60, 440, 260, 450);
            AddWall(580, 440, 540, 450);
            AddWall(1380, 440, 540, 450);

            AddWall(60, 1140, 260, 500);
            AddWall(580, 1140, 540, 500);
            AddWall(1380, 1140, 540, 500);

            // Spawn Moving Civilian Traffic Cars on Roads
            Brush[] colors = { Brushes.Crimson, Brushes.Orange, Brushes.Gold, Brushes.MediumPurple, Brushes.Cyan, Brushes.WhiteSmoke };
            (Point pos, double angle)[] trafficSpawns =
            {
                (new Point(750, 290), 0),
                (new Point(1550, 350), 180),
                (new Point(650, 990), 0),
                (new Point(1650, 1050), 180),
                (new Point(800, 1740), 0),
                (new Point(420, 650), 90),
                (new Point(480, 1400), 270),
                (new Point(1220, 550), 90),
                (new Point(1280, 1300), 270)
            };

            for (int i = 0; i < trafficSpawns.Length; i++)
            {
                SpawnCar(trafficSpawns[i].pos, trafficSpawns[i].angle, colors[i % colors.Length], isPolice: false, isTraffic: true);
            }

            // Spawn Weapon Pickups around the city
            SpawnPickup(new Point(540, 230), WeaponType.Shotgun, 24);
            SpawnPickup(new Point(1160, 930), WeaponType.Smg, 120);
            SpawnPickup(new Point(360, 1680), WeaponType.RocketLauncher, 8);
            SpawnPickup(new Point(1340, 230), WeaponType.Smg, 120);
            SpawnPickup(new Point(540, 1110), WeaponType.Shotgun, 24);

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

        private Car SpawnCar(Point pos, double angle, Brush color, bool isPolice, bool isTraffic)
        {
            var car = new Car
            {
                Pos = pos,
                Angle = angle,
                BaseColor = color,
                IsPolice = isPolice,
                IsTraffic = isTraffic,
                Speed = isTraffic ? 3.5 : 0,
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
        private void SpawnPickup(Point pos, WeaponType weapon, int ammo)
        {
            string label = weapon switch
            {
                WeaponType.Shotgun => "SG",
                WeaponType.Smg => "SMG",
                WeaponType.RocketLauncher => "RPG",
                _ => "W"
            };
            Brush badgeColor = weapon switch
            {
                WeaponType.Shotgun => Brushes.DarkOrange,
                WeaponType.Smg => Brushes.MediumSeaGreen,
                WeaponType.RocketLauncher => Brushes.Crimson,
                _ => Brushes.Gold
            };
            var border = new Border
            {
                Width = 34,
                Height = 22,
                Background = badgeColor,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    //FontWeight = FontWeight .Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(border, pos.X - 17);
            Canvas.SetTop(border, pos.Y - 11);
            WorldCanvas.Children.Add(border);
            pickups.Add(new Pickup { Visual = border, Pos = pos, Weapon = weapon, Ammo = ammo });
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
            UpdateWeaponFiring();
            UpdatePickups();
            UpdateAllCars();
            UpdatePoliceSpawning();
            UpdateNpcs();
            UpdateProjectiles();
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
        private void UpdateWeaponFiring()
        {
            if (fireCooldown > 0) fireCooldown--;
            if (isMouseDown && fireCooldown <= 0)
            {
                FireCurrentWeapon();
            }
        }
        private void FireCurrentWeapon()
        {
            mousePos = Mouse.GetPosition(WorldCanvas);
            Vector dir = mousePos - playerPos;
            if (dir.Length == 0) return;
            dir.Normalize();
            double baseAngle = Math.Atan2(dir.Y, dir.X);
            switch (currentWeapon)
            {
                case WeaponType.Pistol:
                    SpawnProjectile(playerPos, baseAngle, speed: 16, damage: 25, life: 60, isRocket: false);
                    fireCooldown = 14;
                    break;
                case WeaponType.Shotgun:
                    for (int i = -2; i <= 2; i++)
                    {
                        double spread = baseAngle + (i * 0.09);
                        SpawnProjectile(playerPos, spread, speed: 15, damage: 22, life: 32, isRocket: false);
                    }
                    fireCooldown = 34;
                    ConsumeAmmo(1);
                    break;
                case WeaponType.Smg:
                    double jitter = baseAngle + ((rng.NextDouble() - 0.5) * 0.12);
                    SpawnProjectile(playerPos, jitter, speed: 18, damage: 16, life: 55, isRocket: false);
                    fireCooldown = 5;
                    ConsumeAmmo(1);
                    break;
                case WeaponType.RocketLauncher:
                    SpawnProjectile(playerPos, baseAngle, speed: 12, damage: 150, life: 90, isRocket: true);
                    fireCooldown = 45;
                    ConsumeAmmo(1);
                    break;
            }
            sfxShoot.Play();
        }
        private void ConsumeAmmo(int amount)
        {
            if (weaponAmmo < 0) return;
            weaponAmmo -= amount;
            if (weaponAmmo <= 0)
            {
                currentWeapon = WeaponType.Pistol;
                weaponAmmo = -1;
            }
        }
        private void SpawnProjectile(Point origin, double angleRad, double speed, double damage, int life, bool isRocket)
        {
            var shape = new Ellipse
            {
                Width = isRocket ? 11 : 6,
                Height = isRocket ? 11 : 6,
                Fill = isRocket ? Brushes.OrangeRed : Brushes.Yellow,
                Stroke = isRocket ? Brushes.White : null,
                StrokeThickness = isRocket ? 1.5 : 0
            };
            WorldCanvas.Children.Add(shape);
            projectiles.Add(new Projectile
            {
                Shape = shape,
                Pos = origin,
                Velocity = new Vector(Math.Cos(angleRad) * speed, Math.Sin(angleRad) * speed),
                Life = life,
                Damage = damage,
                IsRocket = isRocket
            });
        }
        private void UpdatePickups()
        {
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var p = pickups[i];
                if ((playerPos - p.Pos).Length < 28)
                {
                    currentWeapon = p.Weapon;
                    weaponAmmo = p.Ammo;
                    AddScore(75);
                    sfxPickup.Play();
                    WorldCanvas.Children.Remove(p.Visual);
                    pickups.RemoveAt(i);
                }
            }
        }
        private void UpdateAllCars()
        {
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                var car = cars[i];
                // 1. Police AI Chase
                if (car.IsPolice && car != currentCar)
                {
                    if (wantedLevel > 0)
                    {
                        Vector toPlayer = playerPos - car.Pos;
                        double targetAngle = Math.Atan2(toPlayer.Y, toPlayer.X) * 180.0 / Math.PI;
                        double angleDiff = NormalizeAngle(targetAngle - car.Angle);
                        car.Angle += Math.Clamp(angleDiff, -4.0, 4.0);
                        car.Speed = Math.Min(car.Speed + 0.25, 8.5 + wantedLevel * 0.4);
                        car.Shape.Stroke = (DateTime.Now.Millisecond / 150) % 2 == 0 ? Brushes.Red : Brushes.DeepSkyBlue;
                    }
                    else
                    {
                        car.Speed *= 0.94;
                    }
                }
                // 2. Civilian Traffic AI Cruise & Intersection Turning
                else if (car.IsTraffic && car != currentCar)
                {
                    car.Speed = 3.5;
                    if (car.TurnCooldown > 0) car.TurnCooldown--;
                    // Check if near a road intersection and randomly turn 90 degrees
                    bool nearHorizRoad = Math.Abs(car.Pos.Y - 320) < 25 || Math.Abs(car.Pos.Y - 1020) < 25 || Math.Abs(car.Pos.Y - 1770) < 25;
                    bool nearVertRoad = Math.Abs(car.Pos.X - 450) < 25 || Math.Abs(car.Pos.X - 1250) < 25;
                    if (nearHorizRoad && nearVertRoad && car.TurnCooldown == 0 && rng.Next(0, 100) < 6)
                    {
                        car.Angle += (rng.Next(0, 2) == 0 ? 90 : -90);
                        car.TurnCooldown = 50;
                    }
                }
                // 3. Player or Parked Car Friction
                else
                {
                    car.Speed *= (car == currentCar) ? 0.96 : 0.94;
                }
                // Move car
                double rad = car.Angle * Math.PI / 180.0;
                Point nextPos = new Point(
                car.Pos.X + Math.Cos(rad) * car.Speed,
                car.Pos.Y + Math.Sin(rad) * car.Speed
                );
                Rect carBox = new Rect(nextPos.X - 16, nextPos.Y - 16, 32, 32);
                // Wall or Map Edge Collision
                if (HitsWall(carBox) || !IsInsideWorld(nextPos))
                {
                    if (car.IsTraffic && car != currentCar)
                    {
                        // Traffic car turns around cleanly at road dead-ends
                        car.Angle += 180;
                    }
                    else
                    {
                        double impact = Math.Abs(car.Speed);
                        if (impact > 2.5)
                        {
                            DamageCar(car, impact * 2.5);
                            if (car == currentCar)
                            {
                                AddScore((int)(impact * 5));
                                sfxCrash.Play();
                            }
                        }
                        car.Speed = -car.Speed * 0.5;
                    }
                }
                else
                {
                    car.Pos = nextPos;
                }
                // Car-to-Car Collisions
                for (int j = i - 1; j >= 0; j--)
                {
                    var other = cars[j];
                    Vector diff = car.Pos - other.Pos;
                    if (diff.Length < 34)
                    {
                        double relSpeed = Math.Abs(car.Speed) + Math.Abs(other.Speed);
                        if (relSpeed > 2.2)
                        {
                            DamageCar(car, relSpeed * 2.5);
                            DamageCar(other, relSpeed * 2.5);
                            if (car == currentCar || other == currentCar)
                            {
                                AddScore((int)(relSpeed * 15));
                                IncreaseWanted(1);
                                sfxCrash.Play();
                            }
                        }
                        if (diff.Length > 0) diff.Normalize();
                        car.Pos += diff * 4;
                        other.Pos -= diff * 4;
                        car.Speed *= -0.4;
                        other.Speed *= -0.4;
                    }
                }
                // Traffic or Cop Car hits Player on foot
                if (currentCar == null && Math.Abs(car.Speed) > 3.0 && (car.Pos - playerPos).Length < 25)
                {
                    if (car.IsPolice)
                        TriggerGameOver();
                    else
                        car.Speed = 0; // Civilian traffic brakes for pedestrians
                }
                // Car runs over NPC pedestrian
                for (int n = npcs.Count - 1; n >= 0; n--)
                {
                    if (Math.Abs(car.Speed) > 2.5 && (npcs[n].Pos - car.Pos).Length < 24)
                    {
                        WorldCanvas.Children.Remove(npcs[n].Shape);
                        npcs.RemoveAt(n);
                        SpawnNpc();
                        if (car == currentCar)
                        {
                            AddScore(100);
                            IncreaseWanted(1);
                        }
                    }
                }
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
                car.Shape.Fill = Brushes.DarkSlateGray;
        }
        private void ExplodeCar(Car car)
        {
            WorldCanvas.Children.Remove(car.Shape);
            TriggerBlast(car.Pos, radius: 95);
            AddScore(car.IsPolice ? 500 : 250);
            IncreaseWanted(1);
            if (car == currentCar)
            {
                currentCar = null;
                TriggerGameOver();
            }
            // Respawn replacement traffic or cop car
            Point respawn = new Point(rng.Next(450, 1600), 290);
            SpawnCar(respawn, 0, car.IsPolice ? Brushes.Black : car.BaseColor, car.IsPolice, car.IsTraffic);
        }
        private void TriggerBlast(Point center, double radius)
        {
            sfxExplosion.Play();
            var shape = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.OrangeRed,
                Stroke = Brushes.Yellow,
                StrokeThickness = 3,
                Opacity = 0.95
            };
            WorldCanvas.Children.Add(shape);
            explosions.Add(new Explosion { Shape = shape, Pos = center });
            // Blast damages nearby NPCs, Cars, and Player
            for (int i = npcs.Count - 1; i >= 0; i--)
            {
                if ((npcs[i].Pos - center).Length < radius)
                {
                    WorldCanvas.Children.Remove(npcs[i].Shape);
                    npcs.RemoveAt(i);
                    AddScore(100);
                    SpawnNpc();
                }
            }
            foreach (var c in cars)
            {
                if ((c.Pos - center).Length < radius && c.Health > 0)
                    DamageCar(c, 75);
            }
            if (currentCar == null && (playerPos - center).Length < radius * 0.85)
            {
                TriggerGameOver();
            }
        }
        private void UpdatePoliceSpawning()
        {
            if (wantedLevel <= 0) return;
            sirenTimer++;
            if (sirenTimer > 95)
            {
                sfxSiren.Play();
                sirenTimer = 0;
            }
            wantedDecayTimer++;
            if (wantedDecayTimer > 650)
            {
                wantedLevel--;
                wantedDecayTimer = 0;
            }
            int activeCops = 0;
            foreach (var c in cars)
                if (c.IsPolice) activeCops++;
            if (activeCops < wantedLevel)
            {
                double angle = rng.NextDouble() * Math.PI * 2;
                Point spawn = new Point(
                Math.Clamp(playerPos.X + Math.Cos(angle) * 650, 80, 1920),
                Math.Clamp(playerPos.Y + Math.Sin(angle) * 650, 80, 1920)
                );
                if (!HitsWall(new Rect(spawn.X - 20, spawn.Y - 20, 40, 40)))
                {
                    SpawnCar(spawn, 0, Brushes.Black, isPolice: true, isTraffic: false);
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
            }
        }
        private void UpdateProjectiles()
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                p.Pos += p.Velocity;
                p.Life--;
                Rect projBox = new Rect(p.Pos.X - 3, p.Pos.Y - 3, 6, 6);
                bool hitSomething = false;
                // Hit NPCs
                for (int j = npcs.Count - 1; j >= 0; j--)
                {
                    if ((npcs[j].Pos - p.Pos).Length < 14)
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
                // Hit Cars
                if (!hitSomething)
                {
                    for (int j = cars.Count - 1; j >= 0; j--)
                    {
                        var car = cars[j];
                        if (car == currentCar) continue;
                        if ((car.Pos - p.Pos).Length < 25)
                        {
                            DamageCar(car, p.Damage);
                            AddScore(25);
                            if (car.IsPolice) IncreaseWanted(1);
                            hitSomething = true;
                            break;
                        }
                    }
                }
                if (p.Life <= 0 || hitSomething || HitsWall(projBox))
                {
                    if (p.IsRocket)
                        TriggerBlast(p.Pos, radius: 110);
                    WorldCanvas.Children.Remove(p.Shape);
                    projectiles.RemoveAt(i);
                    continue;
                }
                Canvas.SetLeft(p.Shape, p.Pos.X - p.Shape.Width / 2);
                Canvas.SetTop(p.Shape, p.Pos.Y - p.Shape.Height / 2);
            }
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
        private void AddScore(int points) => score += points;
        private void IncreaseWanted(int amount)
        {
            wantedLevel = Math.Min(5, wantedLevel + amount);
            wantedDecayTimer = 0;
        }
        private void TriggerGameOver()
        {
            isGameOver = true;
            isMouseDown = false;
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
            string ammoStr = weaponAmmo < 0 ? "∞" : weaponAmmo.ToString();
            WeaponText.Text = $"WEAPON: {currentWeapon.ToString().ToUpper()} ({ammoStr})";
            if (currentCar != null)
            {
                CarHpText.Text = $"VEHICLE HEALTH: {Math.Max(0, (int)currentCar.Health)}%";
                CarHpText.Foreground = currentCar.Health < 35 ? Brushes.Red : Brushes.LightGreen;
            }
            else
            {
                CarHpText.Text = "ON FOOT (Press F near a car to hijack)";
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
                    playerPos = new Point(currentCar.Pos.X, currentCar.Pos.Y - 28);
                    currentCar = null;
                }
                else
                {
                    Car closest = null;
                    double minDist = 54;
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
                        currentCar.IsTraffic = false; // Hijacked car stops following traffic AI!
                        AddScore(50);
                        if (closest.IsPolice) IncreaseWanted(2);
                    }
                }
            }
        }
        private void Window_KeyUp(object sender, KeyEventArgs e) => pressedKeys.Remove(e.Key);
        private void Window_MouseMove(object sender, MouseEventArgs e) => mousePos = e.GetPosition(WorldCanvas);
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isGameOver || e.LeftButton != MouseButtonState.Pressed) return;
            isMouseDown = true;
        }
        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released)
                isMouseDown = false;
        }
    }
}