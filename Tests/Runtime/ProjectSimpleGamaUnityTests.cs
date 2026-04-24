using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.Runtime
{
    public class ProjectSimpleGamaUnityTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [TearDown]
        public void Teardown()
        {
        }

        [UnityTest]
        public IEnumerator TestMethod_TestingHow_TestResult()
        {
            // Arrange

            // Act
            yield return null;

            // Assert
            Assert.AreEqual(1, 1);
        }
    }
}
