//! Markdown Conversion module
//!
//! Provides functionality to convert PDF documents to Markdown format
//! with support for Japanese vertical text, figures, tables, and more.
//!
//! # Issue #36 Implementation
//!
//! This module provides complete PDF to Markdown conversion with:
//!
//! - OCR integration (YomiToku)
//! - Reading order detection (vertical/horizontal)
//! - Figure and table extraction
//! - Heading level estimation
//! - Optional external API validation

mod converter;
mod element_detect;
mod reading_order;
mod renderer;
mod types;

pub mod api_validate;

// Re-export public API
pub use api_validate::{ApiValidator, ValidationProvider, ValidationResult};
pub use converter::{MarkdownConversionResult, MarkdownConverter};
pub use element_detect::{DetectedElement, ElementDetector, ElementType, TableStructure};
pub use reading_order::{ReadingOrderOptions, ReadingOrderSorter, TextDirection};
pub use renderer::{MarkdownRenderOptions, MarkdownRenderer};
pub use types::{
    BoundingBox, MarkdownError, MarkdownOptions, MarkdownOptionsBuilder, PageContent, TextBlock,
    TextDirectionOption,
};
