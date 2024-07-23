using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using Newtonsoft.Json;

namespace MineTooltips
{
    public class ModEntry : Mod
    {
        private static readonly Vector2 TooltipOffset = new(Game1.tileSize / 2);
        private static readonly Rectangle TooltipSourceRect = new(0, 256, 60, 60);
        private const int TooltipBorderSize = 12;
        private const int Padding = 5;
        private const int IconSize = 32;
        private const double HoverDelay = 500;

        private MineElevatorMenu? currentElevatorMenu;
        private double lastHoverStartTime;
        private ClickableComponent? lastHoveredButton;

        private readonly Dictionary<string, Texture2D> mobTextures = new();
        private readonly Dictionary<string, List<string>> floorMonsters = new();
        private readonly Dictionary<int, RewardData> floorRewards = new();

        public override void Entry(IModHelper helper)
        {
            try
            {
                LoadMonsterData(helper);

                helper.Events.Display.MenuChanged += OnMenuChanged;
                helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
                helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error during mod initialization: {ex.Message}", LogLevel.Error);
            }
        }

        // Loads monster data from the JSON file
        private void LoadMonsterData(IModHelper helper)
        {
            try
            {
                string path = Path.Combine(helper.DirectoryPath, "assets", "floor_data.json");
                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<FloorData>(json);

                if (data == null)
                {
                    Monitor.Log("No monster data found in JSON", LogLevel.Warn);
                    return;
                }

                // Load textures for monsters
                foreach (var monster in data.Monsters)
                {
                    try
                    {
                        var texture = helper.ModContent.Load<Texture2D>($"assets/{monster.IconPath}");
                        mobTextures[monster.Name] = texture;
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log($"Failed to load texture for {monster.Name}: {ex.Message}", LogLevel.Error);
                    }
                }

                // Populate floorMonsters with floors and their monsters
                foreach (var floor in data.Floors)
                {
                    floorMonsters[floor.Range] = floor.Monsters;
                }

                // Load reward data
                if (data.Rewards != null)
                {
                    foreach (var reward in data.Rewards)
                    {
                        floorRewards[reward.RewardFloor] = reward;
                        try
                        {
                            var texture = helper.ModContent.Load<Texture2D>($"assets/{reward.RewardIconPath}");
                            mobTextures[reward.RewardName] = texture;
                        }
                        catch (Exception ex)
                        {
                            Monitor.Log($"Failed to load texture for {reward.RewardName}: {ex.Message}", LogLevel.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error loading monster data: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            currentElevatorMenu = e.NewMenu as MineElevatorMenu;
            if (currentElevatorMenu == null)
            {
                lastHoveredButton = null;
                lastHoverStartTime = 0;
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (currentElevatorMenu == null) return;

            try
            {
                foreach (var button in currentElevatorMenu.elevators)
                {
                    if (button.containsPoint(Game1.getMouseX(), Game1.getMouseY()))
                    {
                        if (lastHoveredButton != button)
                        {
                            lastHoveredButton = button;
                            lastHoverStartTime = Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
                        }
                        return;
                    }
                }
                lastHoveredButton = null;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error accessing elevator buttons: {ex.Message}", LogLevel.Error);
                currentElevatorMenu = null;
            }
        }

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            if (currentElevatorMenu == null || lastHoveredButton == null) return;

            try
            {
                if (Game1.currentGameTime.TotalGameTime.TotalMilliseconds - lastHoverStartTime < HoverDelay) return;

                int floorNumber = int.Parse(lastHoveredButton.name);

                if (IsRewardFloor(floorNumber))
                {
                    DrawRewardTooltip(e.SpriteBatch, Game1.smallFont, floorNumber);
                }
                else
                {
                    var (monsters, range) = GetMonstersForFloor(floorNumber);
                    DrawTooltip(e.SpriteBatch, Game1.smallFont, range, monsters);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in OnRenderedActiveMenu: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        private bool IsRewardFloor(int floor) => floor % 10 == 0;

        // Gets the monsters for the specified floor
        private (List<string> Monsters, string Range) GetMonstersForFloor(int floor)
        {
            string range = $"{(floor - 1) / 10 * 10 + 1}-{(floor - 1) / 10 * 10 + 9}";
            return (floorMonsters.TryGetValue(range, out var monsters) ? monsters : new List<string>(), range);
        }

        // Draws the tooltip for monsters
        private void DrawTooltip(SpriteBatch spriteBatch, SpriteFont font, string range, List<string> monsters)
        {
            try
            {
                string monstersLabel = $"Monsters: (Floors {range})";
                Vector2 monstersLabelSize = font.MeasureString(monstersLabel);

                // Determine the maximum width of the mob names; if no monsters, use width of "*No monsters on this floor*"
                float maxMobTextWidth = monsters.Any() ? monsters.Max(mob => font.MeasureString(mob).X) : font.MeasureString("*No monsters on this floor*").X;

                // Calculate the total height needed for the tooltip
                // If there are monsters, include their icons and padding; otherwise, use padding for the "no monsters" message
                int totalHeight = (int)monstersLabelSize.Y + TooltipBorderSize * 2 + Padding + (monsters.Any() ? Padding * monsters.Count + IconSize * monsters.Count : Padding + (int)font.MeasureString("*No monsters on this floor*").Y);

                // Calculate the content width based on the maximum mob text width and icon size if applicable
                float contentWidth = Math.Max(monstersLabelSize.X, maxMobTextWidth + IconSize + Padding);
                Vector2 outerSize = new(contentWidth + TooltipBorderSize * 2 + Padding * 2, totalHeight);

                // Determine the position for the tooltip, ensuring it fits within the screen boundaries and is near the mouse cursor
                float x = Game1.getMouseX() - TooltipOffset.X - outerSize.X;
                float y = Game1.getMouseY() + TooltipOffset.Y;

                // Adjust position to ensure the tooltip fits within the screen boundaries
                x = Math.Max(0, Math.Min(x, Game1.uiViewport.Width - outerSize.X));
                y = Math.Max(0, Math.Min(y, Game1.uiViewport.Height - outerSize.Y));

                // Draw the tooltip box
                IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, TooltipSourceRect, (int)x, (int)y, (int)outerSize.X, (int)outerSize.Y, Color.White);

                // Draw the monsters label text
                float textY = y + TooltipBorderSize + Padding;
                Utility.drawTextWithShadow(spriteBatch, monstersLabel, font, new Vector2(x + TooltipBorderSize + Padding, textY), Game1.textColor);
                textY += monstersLabelSize.Y + Padding;

                if (monsters.Any())
                {
                    foreach (var mob in monsters)
                    {
                        float iconX = x + TooltipBorderSize + Padding;
                        float textX = iconX + IconSize + Padding;

                        // Draw the mob icon if available
                        if (mobTextures.TryGetValue(mob, out Texture2D icon) && icon != null)
                        {
                            // Calculate scale to fit the icon within the designated space
                            float scale = Math.Min((float)IconSize / icon.Width, (float)IconSize / icon.Height);
                            int scaledWidth = (int)(icon.Width * scale);
                            int scaledHeight = (int)(icon.Height * scale);

                            // Center the icon within the IconSize space
                            int offsetX = (IconSize - scaledWidth) / 2;
                            int offsetY = (IconSize - scaledHeight) / 2;
                            spriteBatch.Draw(icon, new Rectangle((int)iconX + offsetX, (int)textY + offsetY, scaledWidth, scaledHeight), Color.White);
                        }

                        Utility.drawTextWithShadow(spriteBatch, mob, font, new Vector2(textX, textY), Game1.textColor);
                        textY += IconSize + Padding;
                    }
                }
                else
                {
                    Utility.drawTextWithShadow(spriteBatch, "*No mobs on this floor*", font, new Vector2(x + TooltipBorderSize + Padding, textY), Game1.textColor);
                }
            }
            catch (Exception ex)
            {
                // Log any errors that occur during the drawing of the tooltip
                Monitor.Log($"Error in DrawTooltip: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        // Draws the tooltip for rewards
        private void DrawRewardTooltip(SpriteBatch spriteBatch, SpriteFont font, int floorNumber)
        {
            try
            {
                string rewardLabel = $"Reward: (Floor {floorNumber})";
                Vector2 rewardLabelSize = font.MeasureString(rewardLabel);

                string rewardName = "*No reward on this floor*";
                Texture2D rewardIcon = null;

                // Check if there is a reward for the given floor
                if (floorRewards.TryGetValue(floorNumber, out RewardData reward))
                {
                    rewardName = reward.RewardName;
                    mobTextures.TryGetValue(rewardName, out rewardIcon);
                }
                Vector2 rewardNameSize = font.MeasureString(rewardName);

                // Calculate the total width and height needed for the tooltip
                float contentWidth = Math.Max(rewardLabelSize.X, Math.Max(rewardNameSize.X, font.MeasureString("*No reward on this floor*").X) + (rewardIcon != null ? IconSize + Padding : 0));
                int totalHeight = (int)rewardLabelSize.Y + TooltipBorderSize * 2 + Padding * 3 + Math.Max((int)rewardNameSize.Y, rewardIcon != null ? IconSize : 0);
                Vector2 outerSize = new(contentWidth + TooltipBorderSize * 2 + Padding * 2, totalHeight);

                // Determine the position for the tooltip, ensuring it fits within the screen boundaries, and is near to the position of the mouse cursor
                float x = Game1.getMouseX() - TooltipOffset.X - outerSize.X;
                float y = Game1.getMouseY() + TooltipOffset.Y;

                // Adjust position to ensure the tooltip fits within the screen boundaries
                x = Math.Max(0, Math.Min(x, Game1.uiViewport.Width - outerSize.X));
                y = Math.Max(0, Math.Min(y, Game1.uiViewport.Height - outerSize.Y));

                // Draw the tooltip box
                IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, TooltipSourceRect, (int)x, (int)y, (int)outerSize.X, (int)outerSize.Y, Color.White);

                // Draw the reward label text
                float textY = y + TooltipBorderSize + Padding;
                Utility.drawTextWithShadow(spriteBatch, rewardLabel, font, new Vector2(x + TooltipBorderSize + Padding, textY), Game1.textColor);
                textY += rewardLabelSize.Y + Padding;

                // Determine positions for the reward icon and reward name text within the tooltip box
                float iconX = x + TooltipBorderSize + Padding;
                float textX = iconX + (rewardIcon != null ? IconSize + Padding : 0);

                // Draw the reward icon if it exists
                if (rewardIcon != null)
                {
                    // Calculate scale to fit the icon within the designated space
                    float scale = Math.Min((float)IconSize / rewardIcon.Width, (float)IconSize / rewardIcon.Height);
                    int scaledWidth = (int)(rewardIcon.Width * scale);
                    int scaledHeight = (int)(rewardIcon.Height * scale);

                    // Center the icon within the IconSize space
                    int offsetX = (IconSize - scaledWidth) / 2;
                    int offsetY = (IconSize - scaledHeight) / 2;
                    spriteBatch.Draw(rewardIcon, new Rectangle((int)iconX + offsetX, (int)textY + offsetY, scaledWidth, scaledHeight), Color.White);
                }

                Utility.drawTextWithShadow(spriteBatch, rewardName, font, new Vector2(textX, textY), Game1.textColor);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in DrawRewardTooltip: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        // Data structure for storing floor data
        private class FloorData
        {
            public List<FloorDetails> Floors { get; set; }
            public List<Monster> Monsters { get; set; }
            public List<RewardData> Rewards { get; set; }
        }

        // Data structure for storing floor details
        private class FloorDetails
        {
            public string Range { get; set; }
            public List<string> Monsters { get; set; }
        }

        // Data structure for storing monster details
        private class Monster
        {
            public string Name { get; set; }
            public string IconPath { get; set; }
        }

        // Data structure for storing reward details
        private class RewardData
        {
            public int RewardFloor { get; set; }
            public string RewardName { get; set; }
            public string RewardIconPath { get; set; }
        }
    }
}