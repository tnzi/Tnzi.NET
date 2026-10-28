namespace Tnzi.Data.Snows;

/// <summary>
/// 常规雪花算法
/// </summary>
internal class SnowWorkerM2 : SnowWorkerM1
{
    public SnowWorkerM2(IdGeneratorOptions options) : base(options)
    {

    }

    public override long NextId()
    {
        lock (_SyncLock)
        {
            long currentTimeTick = GetCurrentTimeTick();

            if (_LastTimeTick == currentTimeTick)
            {
                // 先自增再比较：后置自增会在序号等于上限时再放行一个 MaxSeqNumber + 1，
                // 它进位到机器码位，等于相邻机器码的序号 0
                if (++_CurrentSeqNumber > MaxSeqNumber)
                {
                    _CurrentSeqNumber = MinSeqNumber;
                    currentTimeTick = GetNextTimeTick();
                }
            }
            else
            {
                _CurrentSeqNumber = MinSeqNumber;
            }

            if (currentTimeTick < _LastTimeTick)
            {
                throw new Exception(string.Format("Time error for {0} milliseconds", _LastTimeTick - currentTimeTick));
            }

            _LastTimeTick = currentTimeTick;
            var result = ((currentTimeTick << _TimestampShift) + ((long)WorkerId << SeqBitLength) + (uint)_CurrentSeqNumber);

            return result;
        }
    }

}

