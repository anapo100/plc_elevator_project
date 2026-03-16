using System;


namespace ElevatorHMI
{
    /// <summary>
    /// PLC 통신 서비스 — XGCommSocket 래퍼
    /// 메모리맵:
    ///   READ  MW0~MW3(워드), MB10~MB12(바이트), MB20 비트(160~166)
    ///   WRITE MB30 비트(240~247 외부호출), MB31 비트(248~255 내부버튼)
    /// </summary>
    public class PlcService
    {
        private XGCommSocket _comm = new XGCommSocket();
        private bool _connected = false;

        // ── PLC에서 읽은 값 ──
        public int CurrentFloor { get; private set; }   // MW0 (1~5)
        public int TargetFloor { get; private set; }    // MW1 (0~5)
        public int Direction { get; private set; }      // MW2 (0=정지,1=상승,2=하강)
        public int State { get; private set; }          // MW3 (0~6)

        public byte UpCallQueue { get; private set; }   // MB10
        public byte DownCallQueue { get; private set; } // MB11
        public byte InternalQueue { get; private set; } // MB12

        public bool IsEmergency { get; private set; }   // MB20.0
        public bool IsDoorClosed { get; private set; }  // MB20.1
        public bool IsDoorOpening { get; private set; } // MB20.2
        public bool IsDoorOpen { get; private set; }    // MB20.3
        public bool IsDoorClosing { get; private set; } // MB20.4
        public bool CanGoUp { get; private set; }       // MB20.5
        public bool CanGoDown { get; private set; }     // MB20.6

        public bool IsConnected => _connected;

        // ── 접속 / 해제 ──
        public string LastConnectError { get; private set; }

        public bool Connect(string ip, int port)
        {
            LastConnectError = null;
            try
            {
                uint r = _comm.Connect(ip, port);
                _connected = (r == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS);
                if (!_connected) LastConnectError = string.Format("Connect 반환코드={0}", r);
                return _connected;
            }
            catch (Exception ex)
            {
                _connected = false;
                LastConnectError = ex.Message;
                return false;
            }
        }

        public void Disconnect()
        {
            try { _comm.Disconnect(); } catch { }
            _connected = false;
        }

        public bool KeepAlive()
        {
            if (!_connected) return false;
            try
            {
                uint r = _comm.UpdateKeepAlive();
                if (r != (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
                { _connected = false; return false; }
                return true;
            }
            catch { _connected = false; return false; }
        }

        // ── 폴링: PLC → WPF (100ms 주기) ──
        public bool PollAll()
        {
            if (!_connected) return false;
            try
            {
                // 1) MW0~MW3 (4워드)
                UInt16[] words = new UInt16[4];
                uint r = _comm.ReadDataWord('M', 0, 4, false, words);
                if (r != 0) { _connected = false; return false; }
                CurrentFloor = words[0];
                TargetFloor = words[1];
                Direction = words[2];
                State = words[3];

                // 2) MB10~MB12 (3바이트)
                byte[] bytes = new byte[3];
                r = _comm.ReadDataByte('M', 10, 3, bytes);
                if (r != 0) { _connected = false; return false; }
                UpCallQueue = bytes[0];
                DownCallQueue = bytes[1];
                InternalQueue = bytes[2];

                // 3) MB20.0~MB20.6 (7비트, 오프셋 160~166)
                byte[] bits = new byte[7];
                r = _comm.ReadDataBit('M', 160, 7, bits);
                if (r != 0) { _connected = false; return false; }
                IsEmergency = bits[0] != 0;
                IsDoorClosed = bits[1] != 0;
                IsDoorOpening = bits[2] != 0;
                IsDoorOpen = bits[3] != 0;
                IsDoorClosing = bits[4] != 0;
                CanGoUp = bits[5] != 0;
                CanGoDown = bits[6] != 0;

                return true;
            }
            catch { _connected = false; return false; }
        }

        // ── 버튼 쓰기: WPF → PLC ──

        /// <summary>외부 호출 버튼 (MB30.bitIndex, 오프셋 240+bitIndex)</summary>
        public bool WriteHallCall(int bitIndex, bool value)
        {
            return WriteBit(240 + bitIndex, value);
        }

        /// <summary>내부 버튼 (MB31.bitIndex, 오프셋 248+bitIndex)</summary>
        public bool WriteCarCall(int bitIndex, bool value)
        {
            return WriteBit(248 + bitIndex, value);
        }

        private bool WriteBit(int offset, bool value)
        {
            if (!_connected) return false;
            try
            {
                byte[] buf = new byte[] { (byte)(value ? 1 : 0) };
                uint r = _comm.WriteDataBit('M', offset, 1, buf);
                return r == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS;
            }
            catch { return false; }
        }

        public string GetErrorMessage(uint code)
        {
            return _comm.GetReturnCodeString(code);
        }
    }
}
