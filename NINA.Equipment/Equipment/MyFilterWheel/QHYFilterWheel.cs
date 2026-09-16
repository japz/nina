#region "copyright"

/*
    Copyright © 2016 - 2026 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Utility;
using NINA.Profile.Interfaces;
using QHYCCD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Equipment.Equipment.MyFilterWheel {

    public class QHYFilterWheel : BaseINPC, IFilterWheel {
        private QhySdk.QHYCCD_FILTER_WHEEL_INFO Info;
        private bool _connected = false;
        private readonly IProfileService profileService;
        private bool moveRequested = false;
        private string destinationPostition = string.Empty;
        private readonly object stateLock = new object();
        public IQhySdk Sdk { get; set; }

        public QHYFilterWheel(string fwheel, IProfileService profileService) : this(fwheel, profileService, QhySdk.Instance) {
        }

        public QHYFilterWheel(string fwheel, IProfileService profileService, IQhySdk sdk) {
            this.profileService = profileService;
            Sdk = sdk;

            string FWheelId;
            var cameraModel = string.Empty;

            FWheelId = fwheel;
            Sdk.GetModel(FWheelId, out cameraModel);

            Info.Id = FWheelId;
            Sdk.Open(Info.Id);

            if (Sdk.IsCfwPlugged()) {
                Info.Positions = (uint)Sdk.GetControlValue(QhySdk.CONTROL_ID.CONTROL_CFWSLOTSNUM);
            } else {
                Logger.Error($"QHYCFW: {Id} suddenly has no filter wheel!");
                return;
            }

            Info.Name = string.Format($"{cameraModel} {Info.Positions}-Slot Filter Wheel");

            Sdk.Close();

            Logger.Debug($"QHYCFW: Found filter wheel: {Name}");
        }

        public async Task<bool> Connect(CancellationToken token) {
            return await Task.Run(() => {
                byte[] position = new byte[1];

                Sdk.InitSdk();

                Logger.Debug($"QHYCFW: Connecting to filter wheel {Name}");
                Sdk.Open(Info.Id);
                Sdk.InitCamera();

                if (!Sdk.IsCfwPlugged()) {
                    Sdk.Close();

                    string errMessage = $"CFW {Name} is not found on the connected camera!";
                    Logger.Error($"QHYCFW: " + errMessage);
                    throw new InvalidOperationException(errMessage);
                }

                return Connected = true;
            });
        }

        public bool Connected {
            get => _connected;
            private set {
                _connected = value;
                RaisePropertyChanged();
            }
        }

        public string Id => Info.Id;
        public string Name => Info.Name;
        public string DisplayName => Name;
        public string Category => "QHYCCD";
        public string Description => string.Format($"Integrated or 4-pin Filter Wheel on {Info.Id}");
        public string DriverInfo => "Native driver for QHY integrated or 4-pin filter wheels";
        public string DriverVersion => "1.0";

        public int[] FocusOffsets => this.Filters.Select((x) => x.FocusOffset).ToArray();

        public string[] Names => this.Filters.Select((x) => x.Name).ToArray();

        public short Position {
            get {
                lock (stateLock) {
                    byte[] status = new byte[1];
                    uint rv = Sdk.GetCfwStatus(status);
                    string statusString = Encoding.ASCII.GetString(status);
                    Logger.Debug($"QHYCFW: CFW status rc={rv}, raw='{statusString}', destination='{destinationPostition}', requested={moveRequested}");

                    if (rv != QhySdk.QHYCCD_SUCCESS) {
                        Logger.Error($"QHYCFW: Failed to get filter wheel position: rc={rv}");
                        return -1;
                    }

                    // N and / are documented moving/initializing states. Unknown or out-of-range values are not slot 0.
                    if (statusString == "N" || statusString == "/" || !TryParsePosition(statusString, out short position)) {
                        return -1;
                    }

                    if (position < 0 || position >= Info.Positions || (moveRequested && !statusString.Equals(destinationPostition, StringComparison.OrdinalIgnoreCase))) {
                        return -1;
                    }

                    moveRequested = false;
                    destinationPostition = string.Empty;
                    return position;
                }
            }
            set {
                string destination = value.ToString("X1");
                lock (stateLock) {
                    moveRequested = true;
                    destinationPostition = destination;
                    Logger.Debug($"QHYCFW: Moving to position {value} (str: {destination})");

                    uint rv = Sdk.SendOrderToCfw(destination, destination.Length);
                    if (rv != QhySdk.QHYCCD_SUCCESS) {
                        Logger.Error($"QHYCFW: Failed to order move to position {value} (str: {destination}), rc={rv}!");
                        moveRequested = false;
                        destinationPostition = string.Empty;
                        throw new InvalidOperationException($"QHY filter wheel move to position {value} failed (SDK rc={rv})");
                    }
                }

                RaisePropertyChanged();
            }
        }

        private static bool TryParsePosition(string status, out short position) {
            if (short.TryParse(status, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out position)) {
                return true;
            }

            return status.Length == 1 && short.TryParse(status, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out position);
        }

        public IList<string> SupportedActions => [];

        public void Disconnect() {
            Logger.Debug($"QHYCFW: Closing filter wheel {Name}");

            Connected = false;
            Sdk.Close();
            Sdk.ReleaseSdk();
        }

        private readonly object lockObj = new object();
        public AsyncObservableCollection<FilterInfo> Filters {
            get {
                lock (lockObj) {
                    var filtersList = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters;
                    int positions = (int)Info.Positions;

                    var filters = new FilterManager().SyncFiltersWithPositions(filtersList, positions);
                    profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters = filters;
                    return filters;
                }
            }
        }

        public bool HasSetupDialog => false;

        public void SetupDialog() {
        }

        public string Action(string actionName, string actionParameters) {
            throw new NotImplementedException();
        }

        public string SendCommandString(string command, bool raw) {
            throw new NotImplementedException();
        }

        public bool SendCommandBool(string command, bool raw) {
            throw new NotImplementedException();
        }

        public void SendCommandBlind(string command, bool raw) {
            throw new NotImplementedException();
        }
    }
}