﻿using System;
using System.Collections.Generic;
using System.Linq;
#if STRIDE
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
#else
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endif
using OpenTK.Input;
using RocketUI.Input;
using RocketUI.Utilities.Helpers;
using SharpVR;
using Stride.Core;
using Stride.Engine;

namespace RocketUI
{
    public class GuiDrawScreenEventArgs : EventArgs
    {
        public Screen Screen { get; }

        public GameTime GameTime { get; }

        internal GuiDrawScreenEventArgs(Screen screen, GameTime gameTime)
        {
            Screen = screen;
            GameTime = gameTime;
        }
    }

    public class GuiManager : ComponentBase
    {
        public GuiDebugHelper DebugHelper { get; }

        public event EventHandler<GuiDrawScreenEventArgs> DrawScreen;

        public GuiScaledResolution ScaledResolution { get; }
        public GuiFocusHelper      FocusManager     { get; }

        public IGuiRenderer GuiRenderer { get; }

        public InputManager InputManager { get; }
        internal SpriteBatch  SpriteBatch  { get; private set; }

        public GuiSpriteBatch GuiSpriteBatch { get; private set; }

        public List<Screen> Screens { get; } = new List<Screen>();

        public DialogBase ActiveDialog
        {
            get => _activeDialog;
            private set
            {
                var oldValue = _activeDialog;

                if (oldValue != null)
                {
                    base..IsMouseVisible = value != null;
                    Mouse.SetPosition(Game.Window.ClientBounds.Width / 2, Game.Window.ClientBounds.Height / 2);
                    
                    RemoveScreen(oldValue);
                    oldValue.OnClose();
                }

                _activeDialog = value;

                if (value == null)
                    return;
                
                Game.IsMouseVisible = true;
                Mouse.SetPosition(Game.Window.ClientBounds.Width / 2, Game.Window.ClientBounds.Height / 2);
                AddScreen(value);
                value?.OnShow();
            }
        }

        private IServiceProvider ServiceProvider { get; }

        public GuiManager(Game game,
            IServiceProvider   serviceProvider,
            InputManager       inputManager,
            IGuiRenderer       guiRenderer
        ) : base(game)
        {
            ServiceProvider = serviceProvider;
            InputManager = inputManager;
            ScaledResolution = new GuiScaledResolution(game)
            {
                GuiScale = 9999
            };
            ScaledResolution.ScaleChanged += ScaledResolutionOnScaleChanged;

            FocusManager = new GuiFocusHelper(this, InputManager, game.GraphicsDevice);

            GuiRenderer = guiRenderer;
            guiRenderer.ScaledResolution = ScaledResolution;
            SpriteBatch = new SpriteBatch(Game.GraphicsDevice);

            GuiSpriteBatch = new GuiSpriteBatch(guiRenderer, Game.GraphicsDevice, SpriteBatch);
            //  DebugHelper = new GuiDebugHelper(this);
            DebugHelper = new GuiDebugHelper(game, this);
        }

        public void InvokeDrawScreen(Screen screen, GameTime gameTime)
        {
            DrawScreen?.Invoke(this, new GuiDrawScreenEventArgs(screen, gameTime));
        }

        private void ScaledResolutionOnScaleChanged(object sender, UiScaleEventArgs args)
        {
            Init();
        }

        public void SetSize(int width, int height)
        {
            ScaledResolution.ViewportSize = new Size(width, height);
            GuiSpriteBatch.UpdateProjection();

            foreach (var screen in Screens.ToArray())
            {
                if (!screen.SizeToWindow)
                {
                    screen.InvalidateLayout();
                    continue;
                }

                screen.UpdateSize(ScaledResolution.ScaledWidth, ScaledResolution.ScaledHeight);
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            Init();
        }

        public void Init()
        {
            SpriteBatch = new SpriteBatch(Game.GraphicsDevice);
            GuiRenderer.Init(Game.GraphicsDevice, ServiceProvider);
            ApplyFont(GuiRenderer.Font);
            
            GuiSpriteBatch?.Dispose();
            GuiSpriteBatch = new GuiSpriteBatch(GuiRenderer, Game.GraphicsDevice, SpriteBatch);
                  
            SetSize(ScaledResolution.ViewportSize.Width, ScaledResolution.ViewportSize.Height);
        }

        private bool _doInit = true;
        private DialogBase _activeDialog;

        public void ApplyFont(IFont font)
        {
            GuiRenderer.Font = font;
            GuiSpriteBatch.Font = font;

            _doInit = true;
        }

        public void ShowDialog(DialogBase dialog)
        {
            ActiveDialog = dialog;
        }

        public void HideDialog(DialogBase dialog)
        {
            if (ActiveDialog == dialog)
                ActiveDialog = null;
        }

        public void HideDialog<TGuiDialog>() where TGuiDialog : DialogBase
        {
            foreach (var screen in Screens.ToArray())
            {
                if (screen is TGuiDialog dialog)
                {
                    dialog?.OnClose();
                    Screens.Remove(dialog);
                    if (ActiveDialog == dialog)
                        ActiveDialog = Screens.ToArray().LastOrDefault(e => e is TGuiDialog) as DialogBase;
                }
            }
        }

        public void AddScreen(Screen screen)
        {
            screen.GuiManager = this;
            screen.AutoSizeMode = AutoSizeMode.None;
            screen.Anchor = Alignment.Fixed;

            if (screen.SizeToWindow)
                screen.UpdateSize(ScaledResolution.ScaledWidth, ScaledResolution.ScaledHeight);
            else
                screen.InvalidateLayout();

            screen.Init(GuiRenderer);
            Screens.Add(screen);
        }

        public void RemoveScreen(Screen screen)
        {
            Screens.Remove(screen);
            screen.GuiManager = null;
        }

        public bool HasScreen(Screen screen)
        {
            return Screens.Contains(screen);
        }

        public override void Update(GameTime gameTime)
        {
            if (!Enabled)
                return;

            ScaledResolution.Update();

            var screens = Screens.ToArray();

            if (_doInit)
            {
                _doInit = false;

                foreach (var screen in screens)
                {
                    screen?.Init(GuiRenderer, true);
                }
            }

            FocusManager.Update(gameTime);

            foreach (var screen in screens)
            {
                if (screen == null || screen.IsSelfUpdating)
                    continue;

                screen.Update(gameTime);
            }

            // DebugHelper.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            foreach (var screen in Screens.ToArray())
            {
                if (screen == null || screen.IsSelfDrawing)
                    continue;
                
                try
                {
                    GuiSpriteBatch.Begin(screen.IsAutomaticallyScaled);

                    screen.Draw(GuiSpriteBatch, gameTime);

                    DrawScreen?.Invoke(this, new GuiDrawScreenEventArgs(screen, gameTime));
                    //  DebugHelper.DrawScreen(screen);
                }
                finally
                {
                    GuiSpriteBatch.End();
                }
            }
        }
    }
}