﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Common;
using NuGet.Jobs.Validation.PackageSigning.ExtractAndValidateSignature;
using NuGet.Jobs.Validation.PackageSigning.Messages;
using NuGet.Jobs.Validation.PackageSigning.Storage;
using NuGet.Packaging.Signing;
using NuGet.Services.Validation;
using NuGetGallery;
using Xunit;

namespace Validation.PackageSigning.ExtractAndValidateSignature.Tests
{
    public class SignatureValidatorFacts
    {
        public class ValidateAsync
        {
            private readonly Mock<ISignedPackageReader> _packageMock;
            private readonly ValidatorStatus _validation;
            private readonly SignatureValidationMessage _message;
            private readonly CancellationToken _cancellationToken;
            private readonly Mock<IPackageSigningStateService> _packageSigningStateService;
            private VerifySignaturesResult _verifyResult;
            private readonly Mock<IPackageSignatureVerifier> _packageSignatureVerifier;
            private readonly Mock<ISignaturePartsExtractor> _signaturePartsExtractor;
            private readonly Mock<IEntityRepository<Certificate>> _certificates;
            private readonly Mock<ILogger<SignatureValidator>> _logger;
            private readonly SignatureValidator _target;

            public ValidateAsync()
            {
                _packageMock = new Mock<ISignedPackageReader>();
                _packageMock
                    .Setup(x => x.IsSignedAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(false);

                _validation = new ValidatorStatus
                {
                    PackageKey = 42,
                    State = ValidationStatus.NotStarted,
                };
                _message = new SignatureValidationMessage(
                    "NuGet.Versioning",
                    "4.3.0",
                    new Uri("https://example/nuget.versioning.4.3.0.nupkg"),
                    new Guid("b777135f-1aac-4ec2-a3eb-1f64fe1880d5"));
                _cancellationToken = CancellationToken.None;

                _packageSigningStateService = new Mock<IPackageSigningStateService>();

                _verifyResult = new VerifySignaturesResult(true);
                _packageSignatureVerifier = new Mock<IPackageSignatureVerifier>();
                _packageSignatureVerifier
                    .Setup(x => x.VerifySignaturesAsync(It.IsAny<ISignedPackageReader>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => _verifyResult);

                _signaturePartsExtractor = new Mock<ISignaturePartsExtractor>();
                _certificates = new Mock<IEntityRepository<Certificate>>();
                _logger = new Mock<ILogger<SignatureValidator>>();

                _certificates
                    .Setup(x => x.GetAll())
                    .Returns(Enumerable.Empty<Certificate>().AsQueryable());

                _target = new SignatureValidator(
                    _packageSigningStateService.Object,
                    _packageSignatureVerifier.Object,
                    _signaturePartsExtractor.Object,
                    _certificates.Object,
                    _logger.Object);
            }

            private void Validate(ValidationStatus validationStatus, PackageSigningStatus packageSigningStatus)
            {
                Assert.Equal(validationStatus, _validation.State);
                _packageSigningStateService.Verify(
                    x => x.SetPackageSigningState(
                        _validation.PackageKey,
                        _message.PackageId,
                        _message.PackageVersion,
                        packageSigningStatus),
                    Times.Once);
                _packageSigningStateService.Verify(
                    x => x.SetPackageSigningState(
                        It.IsAny<int>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<PackageSigningStatus>()),
                    Times.Once);

                if (validationStatus == ValidationStatus.Succeeded
                    && packageSigningStatus == PackageSigningStatus.Valid)
                {
                    _signaturePartsExtractor.Verify(
                        x => x.ExtractAsync(It.IsAny<ISignedPackageReader>(), It.IsAny<CancellationToken>()),
                        Times.Once);
                }
                else
                {
                    _signaturePartsExtractor.Verify(
                        x => x.ExtractAsync(It.IsAny<ISignedPackageReader>(), It.IsAny<CancellationToken>()),
                        Times.Never);
                }
            }

            [Fact]
            public async Task AcceptsSignedPackagesWithKnownCertificates()
            {
                // Arrange
                await ConfigureKnownSignedPackage(
                    TestResources.SignedPackageLeaf1Reader,
                    TestResources.Leaf1Thumbprint);

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Succeeded, PackageSigningStatus.Valid);
            }

            [Fact]
            public async Task RejectsSignedPackagesWithKnownCertificatesButFailedVerifyResult()
            {
                // Arrange
                await ConfigureKnownSignedPackage(
                    TestResources.SignedPackageLeaf1Reader,
                    TestResources.Leaf1Thumbprint);

                _verifyResult = new VerifySignaturesResult(valid: false);

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Failed, PackageSigningStatus.Invalid);
            }

            [Fact]
            public async Task LogsVerificationErrorsAndWarnings()
            {
                // Arrange
                await ConfigureKnownSignedPackage(
                    TestResources.SignedPackageLeaf1Reader,
                    TestResources.Leaf1Thumbprint);

                var messages = new List<string>();
                _logger
                    .Setup(x => x.Log(
                        It.IsAny<Microsoft.Extensions.Logging.LogLevel>(),
                        It.IsAny<EventId>(),
                        It.IsAny<object>(),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<object, Exception, string>>()))
                    .Callback<Microsoft.Extensions.Logging.LogLevel, EventId, object, Exception, Func<object, Exception, string>>(
                        (ll, eid, state, ex, formatter) =>
                        {
                            messages.Add(formatter(state, ex));
                        });

                _verifyResult = new VerifySignaturesResult(
                    valid: false,
                    results: new[]
                    {
                        new InvalidSignaturePackageVerificationResult(
                            SignatureVerificationStatus.Invalid,
                            new[]
                            {
                                SignatureLog.Issue(
                                    fatal: true,
                                    code: NuGetLogCode.NU3008,
                                    message: "The package integrity check failed."),
                                SignatureLog.Issue(
                                    fatal: false,
                                    code: NuGetLogCode.NU3016,
                                    message: "The package hash uses an unsupported hash algorithm."),
                            })
                    });

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Failed, PackageSigningStatus.Invalid);
                var message = Assert.Single(messages);
                Assert.Equal(
                    $"Signed package {_message.PackageId} {_message.PackageVersion} is blocked for validation " +
                    $"{_message.ValidationId} due to verify failures. Errors: NU3008: The package integrity check " +
                    $"failed. Warnings: NU3016: The package hash uses an unsupported hash algorithm.",
                    message);
            }

            [Fact]
            public async Task RejectsSignedPackagesWithUnknownCertificates()
            {
                // Arrange
                var signatures = await TestResources.SignedPackageLeaf1Reader.GetSignaturesAsync(CancellationToken.None);

                _packageMock
                    .Setup(x => x.IsSignedAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                _packageMock
                    .Setup(x => x.GetSignaturesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(signatures);
                _certificates
                    .Setup(x => x.GetAll())
                    .Returns(new[] { new Certificate { Thumbprint = TestResources.Leaf2Thumbprint } }.AsQueryable());

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Failed, PackageSigningStatus.Invalid);
            }

            [Fact]
            public async Task RejectsSignedPackagesWithNoSignatures()
            {
                // Arrange
                _packageMock
                    .Setup(x => x.IsSignedAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                _packageMock
                    .Setup(x => x.GetSignaturesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Signature>());

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Failed, PackageSigningStatus.Invalid);
            }

            [Fact]
            public async Task RejectsSignedPackagesWithMultipleSignatures()
            {
                // Arrange
                var signatures = (await TestResources.SignedPackageLeaf1Reader.GetSignaturesAsync(CancellationToken.None))
                    .Concat(await TestResources.SignedPackageLeaf2Reader.GetSignaturesAsync(CancellationToken.None))
                    .ToList();

                _packageMock
                    .Setup(x => x.IsSignedAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                _packageMock
                    .Setup(x => x.GetSignaturesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(signatures);
                _certificates
                    .Setup(x => x.GetAll())
                    .Returns(new[] { TestResources.Leaf1Thumbprint, TestResources.Leaf2Thumbprint }
                        .Select(x => new Certificate { Thumbprint = x })
                        .AsQueryable());

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Failed, PackageSigningStatus.Invalid);
            }

            [Fact]
            public async Task AcceptsUnsignedPackages()
            {
                // Arrange
                _packageMock
                    .Setup(x => x.IsSignedAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(false);

                // Act
                await _target.ValidateAsync(
                    _packageMock.Object,
                    _validation,
                    _message,
                    _cancellationToken);

                // Assert
                Validate(ValidationStatus.Succeeded, PackageSigningStatus.Unsigned);
            }

            private async Task ConfigureKnownSignedPackage(ISignedPackageReader package, string thumbprint)
            {
                var signatures = await package.GetSignaturesAsync(CancellationToken.None);

                _packageMock
                    .Setup(x => x.IsSignedAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                _packageMock
                    .Setup(x => x.GetSignaturesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(signatures);
                _certificates
                    .Setup(x => x.GetAll())
                    .Returns(new[] { new Certificate { Thumbprint = thumbprint } }.AsQueryable());
            }
        }
    }
}
