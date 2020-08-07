﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Cepton.SDK
{
    #region Enumerations
    public enum SensorModel : ushort
    {
        HR80T = 1,
        HR80M = 2,
        HR80W = 3,
        SORA_200 = 4,
        VISTA_860_LEGACY = 5,
        HR80T_R2 = 6,
        VISTA_860 = 7,
        FUSION_790 = 8,
        VISTA_M = 9,
        VISTA_X120 = 10,
        SORA_P60 = 11,
        VISTA_P60 = 12,
        VISTA_X15 = 13,
        VISTA_P90 = 14,
        SORA_P90 = 15,
        VISTA_P61 = 16,
        SORA_P61 = 17,

        INVALID_MODEL = 18,
    };

    [Flags]
    public enum ControlFlags : int
    {
        /// Disable networking operations.
        /**
         * Useful for running multiple instances of sdk in different processes.
         * Must pass packets manually to `cepton_sdk::mock_network_receive`.
         */
        DISABLE_NETWORK = 1 << 1,
        /// Disable marking image clipped points as invalid.
        /**
         * Does not affect number of points returned.
         */
        DISABLE_IMAGE_CLIP = 1 << 2,
        /// Disable marking distance clipped points as invalid.
        /**
         * Does not affect number of points returned.
         */
        DISABLE_DISTANCE_CLIP = 1 << 3,
        /// Enable multiple returns.
        /**
         * When set, `cepton_sdk::SensorInformation::return_count` will indicate the
         * number of returns per laser. Can only be set at sdk initialization.
         */
        ENABLE_MULTIPLE_RETURNS = 1 << 4,

        ALL_POINTS = DISABLE_IMAGE_CLIP | DISABLE_DISTANCE_CLIP,
    };

    public enum FrameMode : int
    {
        /// Report points immediately.
        /**
         * This is the default.
         */
        STREAMING = 0,
        /// Report points at fixed time intervals.
        /**
         * Interval controlled by `FrameOptions::frame_length`.
         */
        TIMED = 1,
        /// Report points when the field of view is covered once.
        COVER = 2,
        /// Report points when the scan pattern goes through a full cycle.
        CYCLE = 3,

        MODE_MAX = 3
    };

    [Flags]
    public enum SensorReturnType : byte
    {
        STRONGEST = 1,
        FARTHEST = 2,
        STRONGEST_AND_FARTHEST = 3,
    };
    #endregion

    #region Callback Delegations
    public delegate void FpCeptonSensorErrorCallback(IntPtr handle,
        SensorErrorCode error_code,
        string error_msg,
        IntPtr error_data,
        long error_data_size,
        IntPtr user_data);

    public delegate void FpImageDataCallback(IntPtr handle, int n_points,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SensorImagePoint[] points,
        IntPtr user_data);
    #endregion

    #region Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct SensorImagePoint
    {
        public long timestamp;  ///< unix time [microseconds]
        public float image_x;      ///< x image coordinate
        public float distance;     ///< distance [meters]
        public float image_z;      ///< z image coordinate
        public float intensity;    ///< diffuse reflectance
        public SensorReturnType return_type;
        private byte flags;
        private readonly ushort reserved;

        public bool Valid
        {
            get { return (flags & 1) != 0; }
            set { flags |= 1; }
        }
        public bool Saturated
        {
            get { return (flags & 2) != 0; }
            set { flags |= 2; }
        }
        public override string ToString()
        {
            return string.Format("<{0,7:F4},{1,7:F4}:{2:F2}>", image_x, image_z, distance);
        }

        /// <summary>
        /// Convert to point in 3D space
        /// </summary>
        /// <param name="x">Horizontal</param>
        /// <param name="y">Distance</param>
        /// <param name="z">Vertical</param>
        public void ConvertToPoint(out float x, out float y, out float z)
        {
            double hypotenuse_small = Math.Sqrt(image_x * image_x + image_z * image_z + 1.0f);
            double ratio = distance / hypotenuse_small;
            x = (float)(-image_x * ratio);
            y = (float)ratio;
            z = (float)(-image_z * ratio);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SensorInformation
    {
        public IntPtr handle;
        public ulong serial_number;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 28)] public string model_name;
        public readonly SensorModel model;
        private readonly ushort reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 28)] public string firmware_version;
        public readonly byte firmware_version_major;
        public readonly byte firmware_version_minor;
        private readonly ushort reserved2;

        public float last_reported_temperature;  ///< [celsius]
        public float last_reported_humidity;     ///< [%]
        public float last_reported_age;          ///< [hours]

        /**
         * Time between measurements [microseconds].
         *
         * NOTE: will be changed to seconds in the next SDK release for consistency.
         */
        public float measurement_period;

        public long ptp_ts;  /// [microseconds]

        public byte gps_ts_year;   ///< 0-99 (2017 -> 17)
        public byte gps_ts_month;  ///< 1-12
        public byte gps_ts_day;    ///< 1-31
        public byte gps_ts_hour;   ///< 0-23
        public byte gps_ts_min;    ///< 0-59
        public byte gps_ts_sec;    ///< 0-59

        public byte return_count;
        public byte segment_count;  ///< Number of image segments

        public uint flags;

        public bool Mocked => (flags & 1) != 0;
        public bool PPSConnected => (flags & 2) != 0;
        public bool NMEAConnected => (flags & 4) != 0;
        public bool PTPConnected => (flags & 8) != 0;
        public bool Calibrated => (flags & 16) != 0;
        public uint FirmwareVersion // 4 bytes: Model.Major.Minor.Build (build is always 0
        {
            get
            {
                // TODO: remove special handling for Vista-X120 after it released
                if (firmware_version_major > 0 || model == SensorModel.VISTA_X120)
                    return (uint)(((byte)model << 24) + (firmware_version_major << 16) + (firmware_version_minor << 8));

                // Legacy calculation to be deprecated in next release
                ushort oldver = Convert.ToUInt16(firmware_version.Substring(1), 16);
                byte old_major = (byte)(oldver & 0xFF);
                return (uint)(((byte)model << 24) + (old_major << 16));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FrameOptions
    {
        public long signature;
        public FrameMode mode;
        public float length;

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_create_frame_options")]
        public static extern FrameOptions Create();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDKOptions
    {
        public long signature;
        public ControlFlags control_flags;
        public FrameOptions frame;
        public ushort port;

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_create_options")]
        public static extern SDKOptions Create();
    }
    #endregion

    public static class CeptonSDK
    {
        #region Private interfaces and helpers
        private const int SDK_VERSION = 19;

        private static void _E(SensorErrorCode ec)
        {
            if (ec != SensorErrorCode.SUCCESS)
                throw new CeptonSDKException(ec);
        }

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_initialize")]
        private static extern SensorErrorCode _Initialize(int ver, ref SDKOptions options,
                      FpCeptonSensorErrorCallback cb, IntPtr user_data);

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_is_initialized")]
        private static extern int _IsInitialized(); // 1 for true, 0 for false

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_deinitialize")]
        private static extern SensorErrorCode _DeInitialize();

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_listen_image_frames")]
        private static extern SensorErrorCode _ListenImageFrames(FpImageDataCallback cb, IntPtr user_data);

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_unlisten_image_frames")]
        private static extern SensorErrorCode _UnlistenImageFrames();

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_get_sensor_information")]
        private static extern SensorErrorCode _GetSensorInformation(IntPtr handle, [In, Out] ref SensorInformation info);

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_get_sensor_information_by_index")]
        private static extern SensorErrorCode _GetSensorInformationByIndex(int idx, [In, Out] ref SensorInformation info);

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_get_sensor_handle_by_serial_number")]
        private static extern SensorErrorCode _GetSensorHandleBySerialNumber(ulong serial_number, out IntPtr handle);

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_get_n_sensors")]
        private static extern int _GetSensorCount();

        [DllImport("cepton_sdk.dll", EntryPoint = "cepton_sdk_get_frame_length")]
        private static extern float _GetFrameLength();
        #endregion

        #region Internal higher level functions and frame cache
        public delegate Sensor fpSensorFactory(ref SensorInformation info);
        public static fpSensorFactory SensorFactory = (ref SensorInformation info) => new Sensor(ref info);
        public static void UpdateSensors()
        {
            int ns = _GetSensorCount();
            var handles = new HashSet<ulong>(SensorDictionary.Keys);

            SensorInformation info = new SensorInformation();
            for (int i = 0; i < ns; i++)
            {
                if (_GetSensorInformationByIndex(i, ref info) == SensorErrorCode.SUCCESS)
                {
                    SensorDictionary.GetOrAdd(info.serial_number, (sn) => SensorFactory(ref info));
                    handles.Remove(info.serial_number);
                }
            }
            foreach (ulong key in handles)
            {
                SensorDictionary.TryRemove(key, out _);
            }
        }

        private static readonly FpImageDataCallback imageDataCallback =
            (IntPtr handle, int n_points, SensorImagePoint[] points, IntPtr user_data) =>
            {
                Sensor s = GetSensorByHandle(handle);
                if (s != null)
                    s.AddImagePoints(points);
            };
        //internal static int GetSensorCount() { return _GetSensorCount(); }

        internal static void ListenImageFrames(FpImageDataCallback cb, IntPtr user_data)
        {
            _E(_ListenImageFrames(cb, user_data));
        }

        internal static void UnlistenImageFrames()
        {
            _E(_UnlistenImageFrames());
        }

        public static Sensor GetSensorByHandle(IntPtr handle)
        {
            SensorInformation info = new SensorInformation();
            _E(_GetSensorInformation(handle, ref info));
            if (SensorDictionary.TryGetValue(info.serial_number, out Sensor s))
                return s;
            return SensorDictionary[info.serial_number] = SensorFactory(ref info);
        }
        internal static SensorInformation GetSensorInformation(IntPtr handle)
        {
            SensorInformation info = new SensorInformation();
            _E(_GetSensorInformation(handle, ref info));
            return info;
        }
        #endregion

        public const int MAX_POINTS_PER_PACKET = 373;
        public const int MAX_POINTS_PER_FRAME = 50000;
        public const int MAX_POINTS_PER_SECOND = 1000000;
        public const int MAX_FRAMES_PER_SECOND = 40;

        private static FpCeptonSensorErrorCallback lastErrorCallback;

        public static void Initialize(FpCeptonSensorErrorCallback cb,
            ControlFlags control_flags = ControlFlags.ENABLE_MULTIPLE_RETURNS)
        {
            SDKOptions opt = SDKOptions.Create();
            opt.control_flags = control_flags;
            _E(_Initialize(SDK_VERSION, ref opt, cb, IntPtr.Zero));
            // Start listening to image points
            ListenImageFrames(imageDataCallback, IntPtr.Zero);
            lastErrorCallback = cb;
        }

        public static bool IsInitialized => _IsInitialized() != 0;

        public static void DeInitialize()
        {
            UnlistenImageFrames();
            _E(_DeInitialize());
            lastErrorCallback = null;
        }

        // Sensors lookup by serial number
        public static IEnumerable<Sensor> Sensors => SensorDictionary.Values;
        public static ConcurrentDictionary<ulong, Sensor> SensorDictionary { get; private set; } = new ConcurrentDictionary<ulong, Sensor>();
        public static Sensor GetSensor(ulong serial_number) =>
            SensorDictionary.TryGetValue(serial_number, out Sensor s) ? s : null;

        public static float GetFrameLength() => _GetFrameLength();
    }
}
