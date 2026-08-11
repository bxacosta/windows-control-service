/**
 * Building elements instead of assembling HTML strings. Everything shown here comes from the
 * machine -- executable paths, product names, user names -- and textContent cannot be talked
 * into being markup.
 */

const SVG = 'http://www.w3.org/2000/svg';

/**
 * @param {string} tag
 * @param {Record<string, string | boolean>} attributes `text` sets textContent.
 * @param {Array<Node | string>} children
 */
export function el(tag, attributes = {}, children = []) {
  const node = document.createElement(tag);

  for (const [name, value] of Object.entries(attributes)) {
    if (name === 'text') {
      node.textContent = value;
    } else if (name === 'class') {
      node.className = value;
    } else if (value === true) {
      node.setAttribute(name, '');
    } else if (value !== false && value !== null && value !== undefined) {
      node.setAttribute(name, value);
    }
  }

  node.append(...children);

  return node;
}

/**
 * One icon from the sprite inlined in index.html. Built rather than written as markup for the
 * same reason as everything else here, and namespaced explicitly because createElement would
 * make an HTML element called "svg", which renders as nothing at all.
 *
 * @param {string} name The symbol id, from markup.js.
 */
export function icon(name, extraClass = '') {
  const svg = document.createElementNS(SVG, 'svg');
  svg.setAttribute('class', extraClass ? `icon ${extraClass}` : 'icon');
  svg.setAttribute('aria-hidden', 'true');

  const use = document.createElementNS(SVG, 'use');
  use.setAttribute('href', `#${name}`);
  svg.append(use);

  return svg;
}

/** Points an existing icon at a different symbol, so the element itself survives a re-render. */
export function setIcon(svg, name) {
  svg.firstElementChild?.setAttribute('href', `#${name}`);
}

/** Replaces the children of a container in one go, so the page never shows a half-built list. */
export function replace(container, children) {
  container.replaceChildren(...children);
}
