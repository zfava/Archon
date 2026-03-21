interface Props {
  businessType: string | null;
}

export function ComplianceBanner({ businessType }: Props) {
  if (businessType === 'healthcare') {
    return (
      <div className="ob-hipaa-banner">
        <div className="ob-hipaa-banner-icon">
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
          </svg>
        </div>
        <div className="ob-hipaa-banner-content">
          <strong>HIPAA-Aligned Architecture:</strong>{' '}
          ArchonAI operates within HIPAA-compliant boundaries. All data access is logged to an
          immutable, hash-chained audit trail. Role-based access controls enforce minimum necessary
          access. Multi-factor authentication is available. PHI handling policies are active.
        </div>
      </div>
    );
  }

  if (businessType === 'financial-services') {
    return (
      <div className="ob-hipaa-banner">
        <div className="ob-hipaa-banner-icon">
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
          </svg>
        </div>
        <div className="ob-hipaa-banner-content">
          <strong>Regulatory-Ready Architecture:</strong>{' '}
          ArchonAI provides SOX-compliant audit trails, four-eyes review enforcement, and separation
          of duties — built into the governance layer, not bolted on. GDPR data subject rights are
          implemented with erasure certificates.
        </div>
      </div>
    );
  }

  return null;
}
