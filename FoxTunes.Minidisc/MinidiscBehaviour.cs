﻿using FoxTunes.Interfaces;
using MD.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FoxTunes
{
    [ComponentDependency(Slot = ComponentSlots.UserInterface)]
    public class MinidiscBehaviour : StandardBehaviour, IInvocableComponent, IConfigurableComponent
    {
        public const string VIEW_DISC = "AAAA";

        public const string SET_DISC_TITLE = "BBBB";

        public const string ERASE_DISC = "CCCC";

        public const string SEND_TO_DISC_SP = "DDDD";

        public const string SEND_TO_DISC_LP2 = "EEEE";

        public const string SEND_TO_DISC_LP4 = "FFFF";

        public ICore Core { get; private set; }

        public MinidiscTrackFactory TrackFactory { get; private set; }

        public IPlaylistManager PlaylistManager { get; private set; }

        public IBackgroundTaskEmitter BackgroundTaskEmitter { get; private set; }

        public IReportEmitter ReportEmitter { get; private set; }

        public IUserInterface UserInterface { get; private set; }

        public IConfiguration Configuration { get; private set; }

        public BooleanConfigurationElement Enabled { get; private set; }

        public override void InitializeComponent(ICore core)
        {
            this.Core = core;
            this.TrackFactory = ComponentRegistry.Instance.GetComponent<MinidiscTrackFactory>();
            this.PlaylistManager = core.Managers.Playlist;
            this.BackgroundTaskEmitter = core.Components.BackgroundTaskEmitter;
            this.ReportEmitter = core.Components.ReportEmitter;
            this.UserInterface = core.Components.UserInterface;
            this.Configuration = core.Components.Configuration;
            this.Enabled = this.Configuration.GetElement<BooleanConfigurationElement>(
                MinidiscBehaviourConfiguration.SECTION,
                MinidiscBehaviourConfiguration.ENABLED
            );
            base.InitializeComponent(core);
        }

        public IEnumerable<IInvocationComponent> Invocations
        {
            get
            {
                if (this.Enabled.Value)
                {
                    yield return new InvocationComponent(InvocationComponent.CATEGORY_SETTINGS, VIEW_DISC, Strings.MinidiscBehaviour_ViewDisc, path: Strings.MinidiscBehaviour_Path);
                    yield return new InvocationComponent(InvocationComponent.CATEGORY_SETTINGS, SET_DISC_TITLE, Strings.MinidiscBehaviour_SetDiscTitle, path: Strings.MinidiscBehaviour_Path);
                    yield return new InvocationComponent(InvocationComponent.CATEGORY_SETTINGS, ERASE_DISC, Strings.MinidiscBehaviour_EraseDisc, path: Strings.MinidiscBehaviour_Path, attributes: InvocationComponent.ATTRIBUTE_SEPARATOR);
                    if (this.PlaylistManager.SelectedItems != null && this.PlaylistManager.SelectedItems.Any())
                    {
                        yield return new InvocationComponent(InvocationComponent.CATEGORY_PLAYLIST, SEND_TO_DISC_SP, string.Format(Strings.MinidiscBehaviour_SendToDisc, "SP"), path: Strings.MinidiscBehaviour_Path);
                        yield return new InvocationComponent(InvocationComponent.CATEGORY_PLAYLIST, SEND_TO_DISC_LP2, string.Format(Strings.MinidiscBehaviour_SendToDisc, "LP2"), path: Strings.MinidiscBehaviour_Path);
                        yield return new InvocationComponent(InvocationComponent.CATEGORY_PLAYLIST, SEND_TO_DISC_LP4, string.Format(Strings.MinidiscBehaviour_SendToDisc, "LP4"), path: Strings.MinidiscBehaviour_Path);
                    }
                }
            }
        }

        public Task InvokeAsync(IInvocationComponent component)
        {
            switch (component.Id)
            {
                case VIEW_DISC:
                    return this.ViewDisc();
                case SET_DISC_TITLE:
                    return this.SetDiscTitle();
                case ERASE_DISC:
                    return this.EraseDisc();
                case SEND_TO_DISC_SP:
                    return this.SendToDisc(Compression.None);
                case SEND_TO_DISC_LP2:
                    return this.SendToDisc(Compression.LP2);
                case SEND_TO_DISC_LP4:
                    return this.SendToDisc(Compression.LP4);
            }
#if NET40
            return TaskEx.FromResult(false);
#else
            return Task.CompletedTask;
#endif
        }

        public async Task ViewDisc()
        {
            var task = await this.OpenDisc().ConfigureAwait(false);
            if (task.Disc == null)
            {
                this.UserInterface.Warn(Strings.MinidiscBehaviour_NoDisc);
                return;
            }
            this.Confirm(task.Device, task.Disc, task.Disc);
        }

        public async Task<bool> SetDiscTitle()
        {
            var task = await this.OpenDisc().ConfigureAwait(false);
            if (task.Disc == null)
            {
                this.UserInterface.Warn(Strings.MinidiscBehaviour_NoDisc);
                return false;
            }
            var title = this.UserInterface.Prompt(Strings.MinidiscBehaviour_SetDiscTitle, task.Disc.Title);
            if (string.IsNullOrEmpty(title))
            {
                //Operation cancelled.
                return false;
            }
            return await this.SetDiscTitle(task.Device, title).ConfigureAwait(false);
        }

        public async Task<bool> SetDiscTitle(IDevice device, string title)
        {
            using (var task = new SetDiscTitleTask(device, title))
            {
                task.InitializeComponent(this.Core);
                await this.BackgroundTaskEmitter.Send(task).ConfigureAwait(false);
                await task.Run().ConfigureAwait(false);
                return task.Result != null && task.Result.Status == ResultStatus.Success;
            }
        }

        public async Task<bool> EraseDisc()
        {
            var task = await this.OpenDisc().ConfigureAwait(false);
            if (task.Disc == null)
            {
                this.UserInterface.Warn(Strings.MinidiscBehaviour_NoDisc);
                return false;
            }
            if (!this.UserInterface.Confirm(Strings.MinidiscBehaviour_ConfirmEraseDisc))
            {
                return false;
            }
            return await this.EraseDisc(task.Device).ConfigureAwait(false);
        }

        public async Task<bool> EraseDisc(IDevice device)
        {
            using (var task = new EraseMinidiscTask(device))
            {
                task.InitializeComponent(this.Core);
                await this.BackgroundTaskEmitter.Send(task).ConfigureAwait(false);
                await task.Run().ConfigureAwait(false);
                return task.Result != null && task.Result.Status == ResultStatus.Success;
            }
        }

        public async Task<bool> WriteDisc(IDevice device, IDisc currentDisc, IDisc updatedDisc)
        {
            var percentUsed = updatedDisc.GetCapacity().PercentUsed;
            if (percentUsed > 100)
            {
                Logger.Write(this, LogLevel.Warn, "The disc capactiy will be exceeded: {0}", percentUsed);
                if (!this.UserInterface.Confirm(string.Format(Strings.MinidiscBehaviour_ConfirmWriteDiscWithoutCapacity, percentUsed)))
                {
                    return false;
                }
            }
            else if (!this.UserInterface.Confirm(Strings.MinidiscBehaviour_ConfirmWriteDisc))
            {
                return false;
            }
            var toolManager = new ToolManager();
            var formatManager = new FormatManager(toolManager);
            var actionBuilder = new ActionBuilder(formatManager);
            var actions = actionBuilder.GetActions(device, currentDisc, updatedDisc);
            using (var task = new WriteMinidiscTask(device, actions))
            {
                task.InitializeComponent(this.Core);
                await this.BackgroundTaskEmitter.Send(task).ConfigureAwait(false);
                await task.Run().ConfigureAwait(false);
                return task.Result != null && task.Result.Status == ResultStatus.Success;
            }
        }

        public async Task SendToDisc(Compression compression)
        {
            if (this.PlaylistManager.SelectedItems == null)
            {
                return;
            }
            var playlistItems = this.PlaylistManager.SelectedItems.ToArray();
            if (!playlistItems.Any())
            {
                return;
            }
            var tracks = await this.TrackFactory.GetTracks(playlistItems).ConfigureAwait(false);
            var task = await this.OpenDisc().ConfigureAwait(false);
            if (task.Disc == null)
            {
                this.UserInterface.Warn(Strings.MinidiscBehaviour_NoDisc);
                return;
            }
            var device = task.Device;
            var currentDisc = task.Disc;
            var updatedDisc = currentDisc.Clone();
            var errors = new List<Exception>();
            if (this.IsUntitled(updatedDisc))
            {
                if (!string.Equals(updatedDisc.Title, tracks.Title, StringComparison.OrdinalIgnoreCase))
                {
                    updatedDisc.Title = tracks.Title;
                    Logger.Write(this, LogLevel.Debug, "Setting the disc title: {0}", updatedDisc.Title);
                }
                else
                {
                    Logger.Write(this, LogLevel.Debug, "Keeping existing disc title: {0}", updatedDisc.Title);
                }
            }
            else
            {
                Logger.Write(this, LogLevel.Debug, "Keeping existing disc title: {0}", updatedDisc.Title);
            }
            foreach (var minidiscTrack in tracks.Tracks)
            {
                var track = default(ITrack);
                try
                {
                    track = updatedDisc.Tracks.Add(minidiscTrack.FileName, compression);
                }
                catch (Exception e)
                {
                    Logger.Write(this, LogLevel.Error, "Error adding track \"{0}\" to disc: {1}", minidiscTrack.FileName, e.Message);
                    errors.Add(e);
                    continue;
                }
                track.Name = minidiscTrack.TrackName;
                Logger.Write(this, LogLevel.Debug, "Adding track \"{0}\": Name: {1}, Compression: {2}", track.Location, track.Name, Enum.GetName(typeof(Compression), compression));
            }
            if (errors.Any())
            {
                throw new AggregateException(errors);
            }
            this.Confirm(task.Device, currentDisc, updatedDisc);
        }

        protected virtual async Task<OpenMinidiscTask> OpenDisc()
        {
            using (var task = new OpenMinidiscTask())
            {
                task.InitializeComponent(this.Core);
                await this.BackgroundTaskEmitter.Send(task).ConfigureAwait(false);
                await task.Run().ConfigureAwait(false);
                return task;
            }
        }

        protected virtual void Confirm(IDevice device, IDisc currentDisc, IDisc updatedDisc)
        {
            var report = new MinidiscReport(this, device, currentDisc, updatedDisc);
            report.InitializeComponent(this.Core);
            this.ReportEmitter.Send(report);
        }

        public bool IsUntitled(IDisc disc)
        {
            return string.IsNullOrEmpty(disc.Title) || string.Equals(disc.Title, global::MD.Net.Constants.UNTITLED, StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerable<ConfigurationSection> GetConfigurationSections()
        {
            return MinidiscBehaviourConfiguration.GetConfigurationSections();
        }
    }
}
