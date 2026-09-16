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
            Mock<IQhySdk> sdk = CreateSdk("?");
            QHYFilterWheel wheel = CreateWheel(sdk);

            wheel.Position.Should().Be(-1);
        }

        [Test]
        public void Position_WhenMoveIsRequested_ReportsMovingUntilDestinationIsObserved() {
            string status = "0";
            Mock<IQhySdk> sdk = CreateSdk(status);
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
            Mock<IQhySdk> sdk = CreateSdk(status);
            QHYFilterWheel wheel = CreateWheel(sdk);
            wheel.Position = 1;

            Task<short>[] reads = Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => wheel.Position))
                .ToArray();

            (await Task.WhenAll(reads)).Should().OnlyContain(position => position == -1);
            status = "1";
            wheel.Position.Should().Be(1);
        }

        private static QHYFilterWheel CreateWheel(Mock<IQhySdk> sdk) {
            Mock<IProfileService> profileService = new Mock<IProfileService>();
            return new QHYFilterWheel("camera", profileService.Object, sdk.Object);
        }

        private static Mock<IQhySdk> CreateSdk(string status) {
            Mock<IQhySdk> sdk = new Mock<IQhySdk>();
            string model = "QHY";
            sdk.Setup(x => x.GetModel("camera", out model));
            sdk.Setup(x => x.IsCfwPlugged()).Returns(true);
            sdk.Setup(x => x.GetControlValue(QhySdk.CONTROL_ID.CONTROL_CFWSLOTSNUM)).Returns(7);
            sdk.Setup(x => x.GetCfwStatus(It.IsAny<byte[]>())).Callback<byte[]>(buffer => {
                buffer[0] = Encoding.ASCII.GetBytes(status)[0];
            }).Returns(QhySdk.QHYCCD_SUCCESS);
            sdk.Setup(x => x.SendOrderToCfw(It.IsAny<string>(), It.IsAny<int>())).Returns(QhySdk.QHYCCD_SUCCESS);
            return sdk;
        }

    }
}
