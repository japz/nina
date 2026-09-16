using FluentAssertions;
using Moq;
using NINA.Profile.Interfaces;
using NINA.Equipment.Equipment.MyFilterWheel;
using QHYCCD;
using System.Text;

namespace NINA.Test.Equipment {

    [TestFixture]
    public class QHYFilterWheelTest {
        [Test]
        public void Position_WhenSdkReportsUnknownStatus_ReturnsMovingInsteadOfSlotZero() {
            Mock<IQhySdk> sdk = CreateSdk(() => "?");
            QHYFilterWheel wheel = CreateWheel(sdk);

            wheel.Position.Should().Be(-1);
        }

        [Test]
        public void Position_WhenMoveIsRequested_ReportsMovingUntilDestinationIsObserved() {
            string status = "0";
            Mock<IQhySdk> sdk = CreateSdk(() => status);
            QHYFilterWheel wheel = CreateWheel(sdk);

            wheel.Position.Should().Be(0);
            wheel.Position = 1;
            wheel.Position.Should().Be(-1);

            status = "1";
            wheel.Position.Should().Be(1);
        }

        [Test]
        public async Task Position_ConcurrentReads_DoNotCorruptMoveState() {
            string status = "0";
            Mock<IQhySdk> sdk = CreateSdk(() => status);
            QHYFilterWheel wheel = CreateWheel(sdk);
            wheel.Position = 1;

            Task<short>[] reads = Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => wheel.Position))
                .ToArray();

            (await Task.WhenAll(reads)).Should().OnlyContain(position => position == -1);
            status = "1";
            wheel.Position.Should().Be(1);
        }

        [Test]
        public async Task Position_ConcurrentSdkReads_AreSerialized() {
            int calls = 0;
            int activeCalls = 0;
            int maximumActiveCalls = 0;
            using ManualResetEventSlim firstCallEntered = new ManualResetEventSlim();
            using ManualResetEventSlim releaseFirstCall = new ManualResetEventSlim();
            Mock<IQhySdk> sdk = CreateSdk(() => "0");
            sdk.Setup(x => x.GetCfwStatus(It.IsAny<byte[]>())).Callback<byte[]>(buffer => {
                int call = Interlocked.Increment(ref calls);
                int active = Interlocked.Increment(ref activeCalls);
                int previousMaximum;
                do {
                    previousMaximum = maximumActiveCalls;
                    if (active <= previousMaximum) {
                        break;
                    }
                } while (Interlocked.CompareExchange(ref maximumActiveCalls, active, previousMaximum) != previousMaximum);

                if (call == 1) {
                    firstCallEntered.Set();
                    releaseFirstCall.Wait();
                }

                buffer[0] = (byte)'0';
                Interlocked.Decrement(ref activeCalls);
            }).Returns(QhySdk.QHYCCD_SUCCESS);
            QHYFilterWheel wheel = CreateWheel(sdk);

            Task<short> firstRead = Task.Run(() => wheel.Position);
            firstCallEntered.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            Task<short> secondRead = Task.Run(() => wheel.Position);
            await Task.Delay(100);

            calls.Should().Be(1);
            releaseFirstCall.Set();
            await Task.WhenAll(firstRead, secondRead);
            maximumActiveCalls.Should().Be(1);
        }

        private static QHYFilterWheel CreateWheel(Mock<IQhySdk> sdk) {
            Mock<IProfileService> profileService = new Mock<IProfileService>();
            return new QHYFilterWheel("camera", profileService.Object, sdk.Object);
        }

        private static Mock<IQhySdk> CreateSdk(Func<string> getStatus) {
            Mock<IQhySdk> sdk = new Mock<IQhySdk>();
            string model = "QHY";
            sdk.Setup(x => x.GetModel("camera", out model));
            sdk.Setup(x => x.IsCfwPlugged()).Returns(true);
            sdk.Setup(x => x.GetControlValue(QhySdk.CONTROL_ID.CONTROL_CFWSLOTSNUM)).Returns(7);
            sdk.Setup(x => x.GetCfwStatus(It.IsAny<byte[]>())).Callback<byte[]>(buffer => {
                buffer[0] = Encoding.ASCII.GetBytes(getStatus())[0];
            }).Returns(QhySdk.QHYCCD_SUCCESS);
            sdk.Setup(x => x.SendOrderToCfw(It.IsAny<string>(), It.IsAny<int>())).Returns(QhySdk.QHYCCD_SUCCESS);
            return sdk;
        }

    }
}
