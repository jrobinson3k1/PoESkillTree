﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using log4net;
using POESKillTree.Controls.Dialogs;
using POESKillTree.Model.Builds;
using POESKillTree.Utils;

namespace POESKillTree.Model.Serialization
{
    /// <summary>
    /// Provides services for loading and saving <see cref="IPersistentData"/> instances from/to the file system.
    /// </summary>
    public static class PersistentDataSerializationService
    {
        private const string FileName = "PersistentData";

        private static readonly ILog Log = LogManager.GetLogger(typeof(PersistentDataSerializationService));

        private static readonly string FilePath = AppData.GetFolder(true) + FileName + ".xml";
        private static readonly string BackupPath = AppData.GetFolder(true) + FileName + ".bak";

        public static IPersistentData CreatePersistentData(string importedBuildPath)
        {
            if (!File.Exists(FilePath))
            {
                var deserializer = new PersistentDataDeserializerCurrent();
                return new PersistentData(deserializer, importedBuildPath);
            }

            try
            {
                return Deserialize(FilePath, importedBuildPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(BackupPath))
                    return Deserialize(BackupPath, importedBuildPath);

                var badFilePath = AppData.GetFolder(true) + FileName + "_Bad.xml";
                File.Copy(FilePath, badFilePath, true);

                FileEx.DeleteIfExists(FilePath);
                FileEx.DeleteIfExists(BackupPath);

                Log.Error("Could not deserialize PeristentData file", ex);
                throw new Exception(ex.Message +
                                    "\nYour PersistentData file could not be loaded correctly. It has been moved to " +
                                    badFilePath);
            }
        }

        private static AbstractPersistentData Deserialize(string filePath, string importedBuildPath)
        {
            var deserializer = new IPersistentDataDeserializer[]
            {
                new PersistentDataDeserializerUpTo2210(), new PersistentDataDeserializerCurrent()
            };
            var xmlString = File.ReadAllText(filePath);
            var version = SerializationUtils.DeserializeString<XmlPersistentDataVersion>(xmlString).AppVersion;
            IPersistentDataDeserializer suitableDeserializer;
            if (version == null)
            {
                suitableDeserializer = deserializer.FirstOrDefault(c => c.MinimumDeserializableVersion == null);
            }
            else
            {
                var v = new Version(version);
                suitableDeserializer = deserializer.FirstOrDefault(c =>
                    v.CompareTo(c.MinimumDeserializableVersion) >= 0
                    && v.CompareTo(c.MaximumDeserializableVersion) <= 0);
            }
            if (suitableDeserializer == null)
            {
                throw new Exception(
                    $"No converter available that can deserialize a PersistentData file with version {version}");
            }
            var data = new PersistentData(suitableDeserializer, importedBuildPath);
            suitableDeserializer.DeserializePersistentDataFile(xmlString);
            return data;
        }

        // Creates empty file with language option set.
        [UsedImplicitly]
        public static void CreateSetupTemplate(string path, string language)
        {
            var data = new BarePersistentData {Options = {Language = language}};
            new PersistentDataSerializer(data).Serialize(Path.Combine(path, FileName + ".xml"));
        }


        private class PersistentData : AbstractPersistentData
        {
            private PersistentDataSerializer _serializer;
            private readonly IPersistentDataDeserializer _deserializer;
            private readonly string _importedBuildPath;
            private readonly PersistentDataDeserializerCurrent _currentDeserializer;

            public PersistentData(IPersistentDataDeserializer deserializer, string importedBuildPath)
            {
                _deserializer = deserializer;
                _deserializer.PersistentData = this;
                _importedBuildPath = importedBuildPath;
                _currentDeserializer = deserializer as PersistentDataDeserializerCurrent ??
                                       new PersistentDataDeserializerCurrent {PersistentData = this};
            }

            public override async Task InitializeAsync(IDialogCoordinator dialogCoordinator)
            {
                await _deserializer.InitializeAsync(dialogCoordinator);
                _serializer = new PersistentDataSerializer(this);
                _deserializer.SaveBuildChanges();
                if (!string.IsNullOrEmpty(_importedBuildPath))
                {
                    await _currentDeserializer.ImportBuildAsync(_importedBuildPath);
                }
            }

            public override void Save()
            {
                FileEx.CopyIfExists(FilePath, BackupPath, true);
                try
                {
                    _serializer.Serialize(FilePath);
                }
                catch (Exception e)
                {
                    FileEx.MoveOverwriting(BackupPath, FilePath);
                    Log.Error(
                        "Exception while saving PersistentData. Backup file was restored and changes may be lost.", e);
                }
            }

            public override void SaveBuild(IBuild build)
            {
                _serializer.SerializeBuild(build);
            }

            public override void DeleteBuild(IBuild build)
            {
                _serializer.DeleteBuild(build);
            }

            public override async Task ReloadBuildsAsync()
            {
                await _currentDeserializer.ReloadBuildsAsync();
                _serializer = new PersistentDataSerializer(this);
            }
        }
    }
}