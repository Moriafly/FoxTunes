﻿using FoxTunes.Interfaces;
using MD.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FoxTunes
{
    [ComponentDependency(Slot = ComponentSlots.UserInterface)]
    public class MinidiscTrackFactory : StandardComponent
    {
        //Wav_16_44100_Settings
        public const string ENCODER_PROFILE = "043426B6-7251-45EB-BD74-E46D6ED97A83";

        public static readonly string TempPath = Path.Combine(Path.GetTempPath(), Publication.Product, typeof(MinidiscBehaviour).Name);

        public IEncoder Encoder { get; private set; }

        public IReportEmitter ReportEmitter { get; private set; }

        public IScriptingRuntime ScriptingRuntime { get; private set; }

        public IConfiguration Configuration { get; private set; }

        public TextConfigurationElement DiscTitleScript { get; private set; }

        public TextConfigurationElement TrackNameScript { get; private set; }

        public override void InitializeComponent(ICore core)
        {
            this.Encoder = ComponentRegistry.Instance.GetComponent<IEncoder>();
            this.ReportEmitter = core.Components.ReportEmitter;
            this.ScriptingRuntime = core.Components.ScriptingRuntime;
            this.Configuration = core.Components.Configuration;
            this.DiscTitleScript = this.Configuration.GetElement<TextConfigurationElement>(
                MinidiscBehaviourConfiguration.SECTION,
                MinidiscBehaviourConfiguration.DISC_TITLE_SCRIPT
            );
            this.TrackNameScript = this.Configuration.GetElement<TextConfigurationElement>(
                MinidiscBehaviourConfiguration.SECTION,
                MinidiscBehaviourConfiguration.TRACK_NAME_SCRIPT
            );
            base.InitializeComponent(core);
        }

        public async Task<MinidiscTrackCollection> GetTracks(PlaylistItem[] playlistItems)
        {
            var fileNames = await this.GetWaveFiles(playlistItems).ConfigureAwait(false);
            if (fileNames == null || fileNames.Count == 0)
            {
                return default(MinidiscTrackCollection);
            }
            using (var scriptingContext = this.ScriptingRuntime.CreateContext())
            {
                return new MinidiscTrackCollection(
                    this.GetTitle(scriptingContext, fileNames.Keys),
                    fileNames.Select(
                        pair => new MinidiscTrack(pair.Key, pair.Value, this.GetName(scriptingContext, pair.Key))
                    ).OrderBy(track => track.TrackName).ToArray()
                );
            }
        }

        protected virtual Task<IDictionary<IFileData, string>> GetWaveFiles(IFileData[] fileDatas)
        {
            Logger.Write(this, LogLevel.Debug, "Preparing WAVE files..");
            var fileNames = new Dictionary<IFileData, string>();
            foreach (var fileData in fileDatas)
            {
                try
                {
                    var length = default(TimeSpan);
                    FormatValidator.Default.Validate(fileData.FileName, out length);
                    fileNames[fileData] = fileData.FileName;
                    Logger.Write(this, LogLevel.Debug, "Input file \"{0}\" is supported.");
                }
                catch
                {
                    Logger.Write(this, LogLevel.Debug, "Input file \"{0}\" is not supported, it requires conversion.");
                }
            }
            fileDatas = fileDatas.Except(fileNames.Keys).ToArray();
            if (!fileDatas.Any())
            {
#if NET40
                return TaskEx.FromResult<IDictionary<IFileData, string>>(fileNames);
#else
                return Task.FromResult<IDictionary<IFileData, string>>(fileNames);
#endif
            }
            return this.GetWaveFiles(fileDatas, fileNames);
        }

        protected virtual async Task<IDictionary<IFileData, string>> GetWaveFiles(IFileData[] fileDatas, Dictionary<IFileData, string> fileNames)
        {
            Logger.Write(this, LogLevel.Debug, "Encoding {0} items: WAVE 16 bit, 44.1kHz.", fileDatas.Length);
            var encoderItems = await this.Encoder.Encode(
                fileDatas,
                this.Encoder.GetOutputPath(TempPath),
                ENCODER_PROFILE,
                false
            ).ConfigureAwait(false);
            Logger.Write(this, LogLevel.Debug, "Encoder completed.");
            foreach (var encoderItem in encoderItems)
            {
                if (EncoderItem.WasSkipped(encoderItem))
                {
                    Logger.Write(this, LogLevel.Warn, "Encoder skipped \"{0}\": Re-using existing file: \"{1}\".", encoderItem.InputFileName, encoderItem.OutputFileName);
                }
                else if (encoderItem.Status != EncoderItemStatus.Complete)
                {
                    Logger.Write(this, LogLevel.Warn, "At least one file failed to encode, aborting.");
                    var report = this.Encoder.GetReport(encoderItems);
                    await this.ReportEmitter.Send(report).ConfigureAwait(false);
                    return null;
                }
                var fileData = fileDatas.FirstOrDefault(_fileData => string.Equals(_fileData.FileName, encoderItem.InputFileName, StringComparison.OrdinalIgnoreCase));
                if (fileData == null)
                {
                    Logger.Write(this, LogLevel.Warn, "Failed to determine input file for encoder item: {0}", encoderItem.InputFileName);
                    continue;
                }
                Logger.Write(this, LogLevel.Warn, "Encoded \"{0}\": \"{1}\".", encoderItem.InputFileName, encoderItem.OutputFileName);
                fileNames[fileData] = encoderItem.OutputFileName;
            }
            Logger.Write(this, LogLevel.Debug, "Encoded {0} items.", fileNames.Count);
            return fileNames;
        }

        protected virtual string GetTitle(IScriptingContext scriptingContext, IEnumerable<IFileData> fileDatas)
        {
            var title = default(string);
            foreach (var playlistItem in fileDatas.OfType<PlaylistItem>())
            {
                var runner = new PlaylistItemScriptRunner(scriptingContext, playlistItem, this.DiscTitleScript.Value);
                runner.Prepare();
                var temp = Convert.ToString(runner.Run());
                if (string.IsNullOrEmpty(title) || string.Equals(temp, title, StringComparison.OrdinalIgnoreCase))
                {
                    title = temp;
                }
                else
                {
                    Logger.Write(this, LogLevel.Warn, "Disc title is ambiguous, falling back to : {0}", global::MD.Net.Constants.UNTITLED);
                    return global::MD.Net.Constants.UNTITLED;
                }
            }
            return title;
        }

        protected virtual string GetName(IScriptingContext scriptingContext, IFileData fileData)
        {
            if (fileData is PlaylistItem playlistItem)
            {
                var runner = new PlaylistItemScriptRunner(scriptingContext, playlistItem, this.TrackNameScript.Value);
                runner.Prepare();
                return Convert.ToString(runner.Run());
            }
            throw new NotImplementedException();
        }

        public static void Cleanup()
        {
            if (!Directory.Exists(TempPath))
            {
                //Nothing to do.
                return;
            }
            Logger.Write(typeof(MinidiscBehaviour), LogLevel.Debug, "Cleaning up temp files: {0}", TempPath);
            try
            {
                Directory.Delete(TempPath, true);
            }
            catch (Exception e)
            {
                Logger.Write(typeof(MinidiscBehaviour), LogLevel.Warn, "Failed to cleanup temp files: {0}", e.Message);
            }
        }

        public class MinidiscTrackCollection
        {
            public MinidiscTrackCollection(string title, IEnumerable<MinidiscTrack> tracks)
            {
                this.Title = title;
                this.Tracks = tracks;
            }

            public string Title { get; private set; }

            public IEnumerable<MinidiscTrack> Tracks { get; private set; }
        }

        public class MinidiscTrack
        {
            public MinidiscTrack(IFileData fileData, string fileName, string trackName)
            {
                this.FileData = fileData;
                this.FileName = fileName;
                this.TrackName = trackName;
            }

            public IFileData FileData { get; private set; }

            public string FileName { get; private set; }

            public string TrackName { get; private set; }
        }
    }
}
