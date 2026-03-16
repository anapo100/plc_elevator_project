using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XGCommLib;
using System.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ElevatorHMI
{
    public enum XGCOMM_PRE_DEFINES : uint
    {
        MAX_RW_BIT_SIZE = 64,
        MAX_RW_BYTE_SIZE = 1000,
        MAX_RW_WORD_SIZE = 500,
        DEF_PLC_SERVER_TIME_OUT = 15000,
        DEF_PLC_KEEP_ALIVE_TIME = 10000
    }

    public enum DEF_DATA_TYPE : uint
    {
        DATA_TYPE_BIT = 0,
        DATA_TYPE_BYTE = 1,
        DATA_TYPE_WORD = 2
    }

    public enum XGCOMM_FUNC_RESULT : uint
    {
        RT_XGCOMM_SUCCESS = 0,
        RT_XGCOMM_CAN_NOT_FIND_DLL = 1,
        RT_XGCOMM_FAILED_CONNECT = 2,
        RT_XGCOMM_FAILED_KEEPALIVE = 3,
        RT_XGCOMM_INVALID_COMM_DRIVER = 5,
        RT_XGCOMM_INVALID_POINT = 6,
        RT_XGCOMM_FAILED_RESULT = 10,
        RT_XGCOMM_FAILED_READ = 11,
        RT_XGCOMM_FAILED_WRITE = 12,
        RT_XGCOMM_ABOVE_MAX_BIT_SIZE = 20,
        RT_XGCOMM_ABOVE_MAX_BYTE_SIZE = 21,
        RT_XGCOMM_ABOVE_MAX_WORD_SIZE = 22,
        RT_XGCOMM_BLOW_MIN_SIZE = 23,
        RT_XGCOMM_FAILED_GET_TIMEOUT = 25,
        RT_XGCOMM_FAILED_SET_TIMEOUT = 26,
    }

    class XGCommSocket
    {
        private CommObject20 m_CommDriver = null;
        private Int32 m_nLastCommTime = 0;
        private string m_strIP;
        private long m_lPortNo;
        private Object m_MonitorLock = new System.Object();

        public XGCommSocket() { }
        ~XGCommSocket() { }

        public uint Connect(string strIP, long lPort)
        {
            if ((m_strIP != strIP) || (m_lPortNo != lPort))
            {
                if (this.m_CommDriver != null)
                {
                    this.m_CommDriver.RemoveAll();
                    this.m_CommDriver.Disconnect();
                    this.m_CommDriver = null;
                }
                string strConnection = string.Format("{0}:{1}", strIP, lPort);
                CommObjectFactory20 factory = new CommObjectFactory20();
                this.m_CommDriver = factory.GetMLDPCommObject20(strConnection);
            }
            else
            {
                if (this.m_CommDriver == null)
                {
                    string strConnection = string.Format("{0}:{1}", strIP, lPort);
                    CommObjectFactory20 factory = new CommObjectFactory20();
                    this.m_CommDriver = factory.GetMLDPCommObject20(strConnection);
                }
                else
                {
                    m_CommDriver.Disconnect();
                }
            }

            if (0 == m_CommDriver.Connect(""))
            {
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_CONNECT;
            }

            m_strIP = strIP;
            m_lPortNo = lPort;
            m_nLastCommTime = Environment.TickCount;
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
        }

        public uint Disconnect()
        {
            if (this.m_CommDriver != null)
            {
                this.m_CommDriver.Disconnect();
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
            }
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
        }

        public uint UpdateKeepAlive()
        {
            uint dwTimeSpen;
            uint dwReturn;

            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;

            dwTimeSpen = (uint)TICKS_DIFF(m_nLastCommTime, Environment.TickCount);

            if (dwTimeSpen > (uint)XGCOMM_PRE_DEFINES.DEF_PLC_KEEP_ALIVE_TIME)
            {
                dwReturn = ReadDataBit('F', 0, 1, null);
                if (dwReturn != (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                {
                    if (dwTimeSpen > (uint)XGCOMM_PRE_DEFINES.DEF_PLC_SERVER_TIME_OUT)
                        return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_KEEPALIVE;
                }
                else
                {
                    m_nLastCommTime = Environment.TickCount;
                }
            }
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
        }

        public uint ReadDataBit(char szDeviceType, long lOffsetBit, long lSizeBit, Byte[] pbyRead)
        {
            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
            if (lSizeBit > (uint)XGCOMM_PRE_DEFINES.MAX_RW_BIT_SIZE)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_ABOVE_MAX_BIT_SIZE;
            if (lSizeBit < 1)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_BLOW_MIN_SIZE;

            uint dwReteurn;
            long lRetValue = 0, lCount = 0, lByteOffset, lBitOffset;
            CommObjectFactory20 factory = new CommObjectFactory20();
            XGCommLib.DeviceInfo oDevice;

            Lock();
            this.m_CommDriver.RemoveAll();

            for (lCount = 0; lCount < lSizeBit; lCount++)
            {
                oDevice = factory.CreateDevice();
                oDevice.ucDataType = (byte)'X';
                oDevice.ucDeviceType = (byte)szDeviceType;
                lByteOffset = (lOffsetBit + lCount) / 8;
                lBitOffset = (lOffsetBit + lCount) % 8;
                oDevice.lOffset = (Int32)lByteOffset;
                oDevice.lSize = (Int32)lBitOffset;
                this.m_CommDriver.AddDeviceInfo(oDevice);
            }

            byte[] bufRead = new byte[lSizeBit];
            lRetValue = this.m_CommDriver.ReadRandomDevice(bufRead);
            if (0 == lRetValue)
            {
                dwReteurn = Connect(m_strIP, m_lPortNo);
                if (dwReteurn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                {
                    lRetValue = this.m_CommDriver.ReadRandomDevice(bufRead);
                    if (0 == lRetValue) { UnLock(); return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_READ; }
                }
                else { UnLock(); return dwReteurn; }
            }
            UnLock();

            if (pbyRead != null)
                bufRead.CopyTo(pbyRead, 0);

            m_nLastCommTime = Environment.TickCount;
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
        }

        public uint WriteDataBit(char szDeviceType, long lOffsetBit, long lSizeBit, Byte[] pbyWrite)
        {
            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
            if (lSizeBit > (uint)XGCOMM_PRE_DEFINES.MAX_RW_BIT_SIZE)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_ABOVE_MAX_BIT_SIZE;
            if (lSizeBit < 1)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_BLOW_MIN_SIZE;

            uint dwReteurn;
            long lRetValue = 0, lCount = 0, lByteOffset, lBitOffset;
            CommObjectFactory20 factory = new CommObjectFactory20();

            Lock();
            this.m_CommDriver.RemoveAll();

            for (lCount = 0; lCount < lSizeBit; lCount++)
            {
                XGCommLib.DeviceInfo oDevice = factory.CreateDevice();
                oDevice.ucDataType = (byte)'X';
                oDevice.ucDeviceType = (byte)szDeviceType;
                lByteOffset = (lOffsetBit + lCount) / 8;
                lBitOffset = (lOffsetBit + lCount) % 8;
                oDevice.lOffset = (Int32)lByteOffset;
                oDevice.lSize = (Int32)lBitOffset;
                this.m_CommDriver.AddDeviceInfo(oDevice);
            }

            lRetValue = this.m_CommDriver.WriteRandomDevice(pbyWrite);
            if (0 == lRetValue)
            {
                dwReteurn = Connect(m_strIP, m_lPortNo);
                if (dwReteurn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                {
                    lRetValue = this.m_CommDriver.ReadRandomDevice(pbyWrite);
                    if (0 == lRetValue) { UnLock(); return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_WRITE; }
                }
                else { UnLock(); return dwReteurn; }
            }
            UnLock();

            m_nLastCommTime = Environment.TickCount;
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
        }

        public uint ReadDataByte(char szDeviceType, long lOffsetByte, long lSizeByte, Byte[] pbyRead)
        {
            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
            if (lSizeByte > (uint)XGCOMM_PRE_DEFINES.MAX_RW_BYTE_SIZE)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_ABOVE_MAX_BYTE_SIZE;
            if (lSizeByte < 1)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_BLOW_MIN_SIZE;

            uint dwReteurn;
            long lRetValue = 0;
            CommObjectFactory20 factory = new CommObjectFactory20();

            Lock();
            this.m_CommDriver.RemoveAll();

            XGCommLib.DeviceInfo oDevice = factory.CreateDevice();
            oDevice.ucDataType = (byte)'B';
            oDevice.ucDeviceType = (byte)szDeviceType;
            oDevice.lOffset = (Int32)lOffsetByte;
            oDevice.lSize = (Int32)lSizeByte;
            this.m_CommDriver.AddDeviceInfo(oDevice);

            byte[] bufRead = new byte[lSizeByte];
            lRetValue = this.m_CommDriver.ReadRandomDevice(bufRead);
            if (0 == lRetValue)
            {
                dwReteurn = Connect(m_strIP, m_lPortNo);
                if (dwReteurn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                {
                    lRetValue = this.m_CommDriver.ReadRandomDevice(bufRead);
                    if (0 == lRetValue) { UnLock(); return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_READ; }
                }
                else { UnLock(); return dwReteurn; }
            }
            UnLock();

            if (pbyRead != null)
                bufRead.CopyTo(pbyRead, 0);

            m_nLastCommTime = Environment.TickCount;
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
        }

        public uint WriteDataByte(char szDeviceType, long lOffsetByte, long lSizeByte, Byte[] pbyWrite)
        {
            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
            if (lSizeByte > (uint)XGCOMM_PRE_DEFINES.MAX_RW_BYTE_SIZE)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_ABOVE_MAX_BYTE_SIZE;
            if (lSizeByte < 1)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_BLOW_MIN_SIZE;
            if (pbyWrite == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_POINT;

            uint dwReteurn;
            long lRetValue = 0;
            CommObjectFactory20 factory = new CommObjectFactory20();

            Lock();
            this.m_CommDriver.RemoveAll();

            XGCommLib.DeviceInfo oDevice = factory.CreateDevice();
            oDevice.ucDataType = (byte)'B';
            oDevice.ucDeviceType = (byte)szDeviceType;
            oDevice.lOffset = (Int32)lOffsetByte;
            oDevice.lSize = (Int32)lSizeByte;
            this.m_CommDriver.AddDeviceInfo(oDevice);

            lRetValue = this.m_CommDriver.WriteRandomDevice(pbyWrite);
            if (0 == lRetValue)
            {
                dwReteurn = Connect(m_strIP, m_lPortNo);
                if (dwReteurn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                {
                    lRetValue = this.m_CommDriver.WriteRandomDevice(pbyWrite);
                    if (0 == lRetValue) { UnLock(); return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_WRITE; }
                }
                else { UnLock(); return dwReteurn; }
            }
            UnLock();

            m_nLastCommTime = Environment.TickCount;
            return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
        }

        public uint ReadDataWord(char szDeviceType, long lOffsetWord, long lSizeWord, bool bByteSwap, UInt16[] pwRead)
        {
            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
            if (lSizeWord > (uint)XGCOMM_PRE_DEFINES.MAX_RW_WORD_SIZE)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_ABOVE_MAX_WORD_SIZE;
            if (lSizeWord < 1)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_BLOW_MIN_SIZE;

            uint dwReturn;
            long lCount, lOffsetByte, lSizeByte, lByteOffset;

            lOffsetByte = lOffsetWord * 2;
            lSizeByte = lSizeWord * 2;

            byte[] bufRead = new byte[lSizeByte];
            dwReturn = ReadDataByte(szDeviceType, lOffsetByte, lSizeByte, bufRead);
            if (dwReturn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                if (pwRead != null)
                {
                    if (bByteSwap == true)
                    {
                        for (lCount = 0; lCount < lSizeWord; lCount++)
                        {
                            lByteOffset = lCount * 2;
                            pwRead[lCount] = MAKEWORD(bufRead[lByteOffset + 1], bufRead[lByteOffset]);
                        }
                    }
                    else
                    {
                        System.Buffer.BlockCopy(bufRead, 0, pwRead, 0, (Int32)lSizeByte);
                    }
                }
            }

            if (dwReturn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                m_nLastCommTime = Environment.TickCount;

            return dwReturn;
        }

        public uint WriteDataWord(char szDeviceType, long lOffsetWord, long lSizeWord, bool bByteSwap, UInt16[] pwWrite)
        {
            if (this.m_CommDriver == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER;
            if (lSizeWord > (uint)XGCOMM_PRE_DEFINES.MAX_RW_WORD_SIZE)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_ABOVE_MAX_WORD_SIZE;
            if (lSizeWord < 1)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_BLOW_MIN_SIZE;
            if (pwWrite == null)
                return (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_POINT;

            uint dwReturn;
            long lCount, lOffsetByte, lSizeByte, lByteOffset;

            lOffsetByte = lOffsetWord * 2;
            lSizeByte = lSizeWord * 2;

            byte[] bufWrite = new byte[lSizeByte];
            if (bByteSwap == true)
            {
                for (lCount = 0; lCount < lSizeWord; lCount++)
                {
                    lByteOffset = lCount * 2;
                    bufWrite[lByteOffset] = HIBYTE(pwWrite[lCount]);
                    bufWrite[lByteOffset + 1] = LOBYTE(pwWrite[lCount]);
                }
            }
            else
            {
                System.Buffer.BlockCopy(pwWrite, 0, bufWrite, 0, (Int32)lSizeByte);
            }

            dwReturn = WriteDataByte(szDeviceType, lOffsetByte, lSizeByte, bufWrite);
            if (dwReturn == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                m_nLastCommTime = Environment.TickCount;

            return dwReturn;
        }

        public string GetReturnCodeString(uint uReturnCode)
        {
            switch (uReturnCode)
            {
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS: return "Success";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_CAN_NOT_FIND_DLL: return "XGCommLib.dll을 찾을 수 없음";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_CONNECT: return "PLC 접속 실패";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_KEEPALIVE: return "KeepAlive 실패";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_COMM_DRIVER: return "Comm Driver 미초기화";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_INVALID_POINT: return "배열 포인트 NULL";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_READ: return "읽기 실패";
                case (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_FAILED_WRITE: return "쓰기 실패";
                default: return "알 수 없는 에러 (" + uReturnCode + ")";
            }
        }

        private static byte LOBYTE(UInt16 a) { return ((byte)(a & 0xff)); }
        private static byte HIBYTE(UInt16 a) { return ((byte)(a >> 8)); }
        private static UInt16 MAKEWORD(byte low, byte high) { return (UInt16)((high << 8) | low); }

        private static Int32 TICKS_DIFF(int prev, int cur)
        {
            if (cur >= prev) return cur - prev;
            unchecked { return ((int)0xFFFFFFFF - prev) + 1 + cur; }
        }

        private void Lock() { Monitor.Enter(m_MonitorLock); }
        private void UnLock() { Monitor.Exit(m_MonitorLock); }
    }

    /// <summary>
    /// 앱 시작 시 XGCommLib64.dll COM 등록 여부를 확인하고, 미등록이면 자동으로 regsvr32 실행
    /// </summary>
    static class DllRegistrar
    {
        // COM 등록 시 생성되는 ProgID 키로 등록 여부 판별
        private const string ProgIdKey = @"XGCommLib.CommObjectFactory20";

        public static bool EnsureRegistered(out string message)
        {
            if (IsRegistered())
            {
                message = "XGCommLib64.dll 이미 등록됨";
                return true;
            }

            string dllPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "XGCommLib64.dll");

            if (!System.IO.File.Exists(dllPath))
            {
                message = "XGCommLib64.dll 파일 없음: " + dllPath;
                return false;
            }

            // x86 앱 → SysWOW64의 regsvr32 사용
            string regsvr32 = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
                "regsvr32.exe");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = regsvr32,
                Arguments       = string.Format("/s \"{0}\"", dllPath),
                UseShellExecute = true,
                Verb            = "runas"   // UAC 관리자 권한 요청
            };

            try
            {
                var p = System.Diagnostics.Process.Start(psi);
                p.WaitForExit();
                if (p.ExitCode == 0 && IsRegistered())
                {
                    message = "XGCommLib64.dll 자동 등록 완료";
                    return true;
                }
                message = string.Format("regsvr32 종료코드={0}", p.ExitCode);
                return false;
            }
            catch (Exception ex)
            {
                message = "자동 등록 실패: " + ex.Message;
                return false;
            }
        }

        private static bool IsRegistered()
        {
            using (var key = Registry.ClassesRoot.OpenSubKey(ProgIdKey))
                return key != null;
        }
    }
}
